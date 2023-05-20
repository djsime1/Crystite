//
//  SPDX-FileName: StandaloneFrooxEngineService.cs
//  SPDX-FileCopyrightText: Copyright (c) Jarl Gullberg
//  SPDX-License-Identifier: AGPL-3.0-or-later
//

using System.Reflection;
using FrooxEngine;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Remora.Neos.Headless.Configuration;
using Remora.Neos.Headless.Services;
using WorldStartupParameters = Remora.Neos.Headless.Configuration.WorldStartupParameters;

namespace Remora.Neos.Headless;

/// <summary>
/// Hosts a standalone FrooxEngine instance.
/// </summary>
public class StandaloneFrooxEngineService : BackgroundService
{
    private readonly ILogger<StandaloneFrooxEngineService> _log;
    private readonly NeosHeadlessConfig _config;
    private readonly Engine _engine;
    private readonly ISystemInfo _systemInfo;
    private readonly WorldService _worldService;

    private bool _engineShutdownComplete;

    /// <summary>
    /// Initializes a new instance of the <see cref="StandaloneFrooxEngineService"/> class.
    /// </summary>
    /// <param name="log">The logging instance for this type.</param>
    /// <param name="config">The application configuration.</param>
    /// <param name="engine">The engine.</param>
    /// <param name="systemInfo">Information about the system.</param>
    /// <param name="worldService">The world service.</param>
    public StandaloneFrooxEngineService
    (
        ILogger<StandaloneFrooxEngineService> log,
        IOptions<NeosHeadlessConfig> config,
        Engine engine,
        ISystemInfo systemInfo,
        WorldService worldService
    )
    {
        _log = log;
        _config = config.Value;
        _engine = engine;
        _systemInfo = systemInfo;
        _worldService = worldService;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var assemblyDirectory = Directory.GetParent(Assembly.GetExecutingAssembly().Location)
                                ?? throw new InvalidOperationException();

        _engine.UsernameOverride = _config.UsernameOverride;
        _engine.EnvironmentShutdownCallback = () => _engineShutdownComplete = true;

        await _engine.Initialize
        (
            assemblyDirectory.FullName,
            _config.DataFolder ?? NeosHeadlessConfig.DefaultDataFolder,
            _config.CacheFolder ?? NeosHeadlessConfig.DefaultCacheFolder,
            _systemInfo,
            null,
            true
        );

        var userspaceWorld = Userspace.SetupUserspace(_engine);

        // start the engine update loop so coroutines can proceed
        var engineLoop = EngineLoopAsync(ct);

        await userspaceWorld.Coroutines.StartTask(static async () => await default(ToWorld));

        if (_config.UniverseID is not null)
        {
            _engine.WorldAnnouncer.UniverseId = _config.UniverseID;
            Engine.Config.UniverseId = _config.UniverseID;
        }

        await _engine.GlobalCoroutineManager.StartTask
        (
            static async args =>
            {
                await default(NextUpdate);

                if (!string.IsNullOrWhiteSpace(args.Config.LoginCredential) && !string.IsNullOrWhiteSpace(args.Config.LoginPassword))
                {
                    args.Log.LogInformation("Logging in as {Credential}", args.Config.LoginCredential);

                    // TODO: LoginToken?
                    var login = await args.Engine.Cloud.Login
                    (
                        args.Config.LoginCredential,
                        args.Config.LoginPassword,
                        null,
                        args.Engine.LocalDB.SecretMachineID,
                        false,
                        null,
                        null
                    );

                    if (!login.IsOK)
                    {
                        args.Log.LogWarning("Failed to log in: {Error}", login.Content);
                    }
                    else
                    {
                        args.Log.LogInformation("Logged in successfully");
                    }
                }
            },
            (Config: _config, Log: _log, Engine: _engine)
        );

        await _engine.GlobalCoroutineManager.StartTask
        (
            static async args =>
            {
                await default(NextUpdate);

                foreach (var allowedUrlHost in args.Config.AllowedUrlHosts ?? Array.Empty<Uri>())
                {
                    args.Log.LogInformation
                    (
                        "Allowing host: {Host}, Port: {Port}",
                        allowedUrlHost.Host,
                        allowedUrlHost.Port
                    );

                    args.Engine.Security.AllowHost(allowedUrlHost.Host, allowedUrlHost.Port);
                }
            },
            (Config: _config, Log: _log, Engine: _engine)
        );

        await _engine.LocalDB.WriteVariableAsync
        (
            "Session.MaxConcurrentTransmitJobs",
            _config.MaxConcurrentAssetTransfers
        );

        foreach (var startWorld in _config.StartWorlds ?? Array.Empty<WorldStartupParameters>())
        {
            if (!startWorld.IsEnabled)
            {
                continue;
            }

            var worldStart = await _worldService.StartWorld(startWorld, ct);
            if (!worldStart.IsSuccess)
            {
                _log.LogWarning("Failed to start world: {Reason}", worldStart.Error);
            }
        }

        await engineLoop;
    }

    private async Task EngineLoopAsync(CancellationToken ct = default)
    {
        using var tickTimer = new PeriodicTimer(TimeSpan.FromSeconds(1.0 / _config.TickRate));

        var isShuttingDown = false;
        while (!ct.IsCancellationRequested || !_engineShutdownComplete)
        {
            // Intentional. Ticks should continue until _engineShutdownComplete, so cancelling early here is
            // undesired.
            if (!await tickTimer.WaitForNextTickAsync(CancellationToken.None))
            {
                break;
            }

            try
            {
                _engine.RunUpdateLoop();
            }
            catch (Exception e)
            {
                _log.LogError(e, "Unexpected error during engine update loop");
            }

            if (!ct.IsCancellationRequested || isShuttingDown)
            {
                continue;
            }

            isShuttingDown = true;
            Userspace.ExitNeos(false);
        }
    }
}
