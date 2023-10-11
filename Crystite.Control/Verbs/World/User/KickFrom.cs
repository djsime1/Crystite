//
//  SPDX-FileName: KickFrom.cs
//  SPDX-FileCopyrightText: Copyright (c) Jarl Gullberg
//  SPDX-License-Identifier: AGPL-3.0-or-later
//

using System.Diagnostics.CodeAnalysis;
using CommandLine;
using Crystite.Control.API;
using Crystite.Control.Verbs.Bases;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Remora.Results;

namespace Crystite.Control.Verbs.User;

/// <summary>
/// Kicks a user from a world.
/// </summary>
[UsedImplicitly]
[Verb("kick-from", HelpText = "Kicks a user from a world")]
public sealed class KickFrom : WorldUserVerb
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KickFrom"/> class.
    /// </summary>
    /// <inheritdoc cref="WorldUserVerb(string, string, string, string, ushort, string, OutputFormat)" path="/param" />
    [SuppressMessage("Documentation", "CS1573", Justification = "Copied from base class")]
    public KickFrom
    (
        string? userName,
        string? userID,
        string? worldName,
        string? worldID,
        ushort port,
        string server,
        OutputFormat outputFormat
    )
        : base(userName, userID, worldName, worldID, port, server, outputFormat)
    {
    }

    /// <inheritdoc/>
    public override async ValueTask<Result> ExecuteAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var worldAPI = services.GetRequiredService<HeadlessWorldAPI>();
        var outputWriter = services.GetRequiredService<TextWriter>();

        var getWorldID = await GetTargetWorldIDAsync(worldAPI, ct);
        if (!getWorldID.IsDefined(out var worldID))
        {
            return (Result)getWorldID;
        }

        var getUserID = await GetTargetUserIDAsync(worldAPI, ct);
        if (!getUserID.IsDefined(out var userID))
        {
            return (Result)getUserID;
        }

        var kickUser = await worldAPI.KickUserAsync(worldID, userID, ct);
        if (!kickUser.IsSuccess)
        {
            return kickUser;
        }

        if (this.OutputFormat is OutputFormat.Verbose)
        {
            await outputWriter.WriteLineAsync("User kicked");
        }

        return Result.FromSuccess();
    }
}
