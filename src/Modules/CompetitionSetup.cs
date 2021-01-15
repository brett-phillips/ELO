﻿using Discord;
using Discord.Commands;
using ELO.Models;
using ELO.Services;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ELO.Extensions;
using ELO.Preconditions;

namespace ELO.Modules
{
    [RequireContext(ContextType.Guild)]
    [Preconditions.RequirePermission(PermissionLevel.ELOAdmin)]
    public class CompetitionSetup : ModuleBase<ShardedCommandContext>
    {
        public PremiumService Premium { get; }

        public CompetitionSetup(PremiumService premium, ELOJobs job)
        {
            Premium = premium;
        }

        [Command("SetPrefix", RunMode = RunMode.Sync)]
        [Summary("Set the server's command prefix")]
        public virtual async Task SetPrefixAsync([Remainder]string prefix = null)
        {
            using (var db = new Database())
            {
                var comp = db.GetOrCreateCompetition(Context.Guild.Id);
                comp.Prefix = prefix;
                db.Update(comp);
                db.SaveChanges();
                Handlers.ELOEventHandler.UpdatePrefix(Context.Guild.Id, prefix);
                //await SimpleEmbedAsync($"Prefix has been set to `{prefix ?? "Default"}`");
                await Context.SimpleEmbedAsync($"Prefix has been {(prefix != null ? "set" : "reset")} to `{prefix ?? Program.Prefix}` {(prefix == null ? "(default)" : "")}\n" +
                                 $"All commands now use this prefix.\n" +
                                 $"Example: `{prefix ?? Program.Prefix}Help`", Color.Green);
            }
        }

        [Command("RemovePremium", RunMode = RunMode.Sync)]
        [Summary("Remove a premium subscription")]
        public virtual async Task RemovePremiumAsync(ulong? guildId = null)
        {
            if (guildId == null) guildId = Context.Guild.Id;

            using (var db = new Database())
            {
                var compMatch = db.Competitions.Find(guildId);
                if (compMatch == null) return;

                if (compMatch.PremiumRedeemer != Context.User.Id) return;

                compMatch.PremiumRedeemer = null;
                compMatch.PremiumBuffer = null;
                compMatch.BufferedPremiumCount = null;
                db.Competitions.Update(compMatch);
                db.SaveChanges();
                await Context.SimpleEmbedAsync("Premium subscription has been removed.");
            }
        }

        [Command("MyPremium", RunMode = RunMode.Sync)]
        [Summary("Display your premium servers")]
        public virtual async Task MyPremiumAsync()
        {
            using (var db = new Database())
            {
                var comps = db.Competitions.AsQueryable().Where(x => x.PremiumRedeemer == Context.User.Id).ToArray();
                if (comps.Length == 0)
                {
                    await Context.SimpleEmbedAsync("You do not have any premium redeemed servers.");
                    return;
                }

                if (comps.Length == 1)
                {
                    var server = Context.Client.Guilds.FirstOrDefault(x => x.Id == comps[0].GuildId);
                    if (server == null)
                    {
                        await Context.SimpleEmbedAsync($"Server Not Found: {comps[0].GuildId}");
                    }
                    else
                    {
                        await Context.SimpleEmbedAsync($"Premium Server: {server.Name} [{comps[0].GuildId}]");
                    }
                    return;
                }

                var builder = new StringBuilder();
                bool notFound = false;
                foreach (var comp in comps)
                {
                    var server = Context.Client.Guilds.FirstOrDefault(x => x.Id == comps.First().GuildId);
                    if (server == null)
                    {
                        builder.AppendLine($"Server Not Found: {comp.GuildId}");
                        notFound = true;
                    }
                    else
                    {
                        builder.AppendLine($"Premium Server: {server.Name} [{comp.GuildId}]");
                    }
                }

                if (notFound)
                {
                    builder.AppendLine("You can remove a not found premium server by running the `RemovePremium` command, NOTE: this does not necessarily mean the server does not host the bot, it may indicate that the server is on a different shard of the bot.");
                }

                var premiumLimit = Premium.GetRegistrationLimit(comps[0].GuildId);
                builder.AppendLine($"Premium limit for each server is `{premiumLimit}`");

                await Context.SimpleEmbedAsync(builder.ToString());
            }
        }

        [Command("ClaimPremium", RunMode = RunMode.Sync)]
        [Summary("Claim a patreon premium subscription")]
        public virtual async Task ClaimPremiumAsync()
        {
            await Premium.Claim(Context);
        }

        [Command("RedeemLegacyToken", RunMode = RunMode.Sync)]
        [Summary("Redeem a 16 digit token for the old version of ELO")]
        public virtual async Task RedeemLegacyTokenAsync([Remainder]string token = null)
        {
            if (token == null)
            {
                await Context.SimpleEmbedAsync("This is used to redeem tokens that were created using the old ELO version.", Color.Blue);
                return;
            }

            using (var db = new Database())
            {
                var legacy = db.LegacyTokens.Find(token);
                if (legacy == null)
                {
                    await Context.SimpleEmbedAsync($"Invalid token provided, if you believe this is a mistake please contact support at: {Premium.PremiumConfig.ServerInvite}", Color.Red);
                }
                else
                {
                    var guild = db.GetOrCreateCompetition(Context.Guild.Id);
                    if (guild.LegacyPremiumExpiry == null)
                    {
                        guild.LegacyPremiumExpiry = DateTime.UtcNow + TimeSpan.FromDays(legacy.Days);
                    }
                    else
                    {
                        if (guild.LegacyPremiumExpiry < DateTime.UtcNow)
                        {
                            guild.LegacyPremiumExpiry = DateTime.UtcNow + TimeSpan.FromDays(legacy.Days);
                        }
                        else
                        {
                            guild.LegacyPremiumExpiry += TimeSpan.FromDays(legacy.Days);
                        }
                    }
                    db.Remove(legacy);
                    db.Update(guild);
                    db.SaveChanges();
                    await Context.SimpleEmbedAsync("Token redeemed.", Color.Green);
                }
            }
        }

        [Command("LegacyExpiration", RunMode = RunMode.Sync)]
        [Summary("Displays the expiry date of any legacy subscription")]
        public virtual async Task LegacyExpirationAsync()
        {
            using (var db = new Database())
            {
                var guild = db.GetOrCreateCompetition(Context.Guild.Id);
                if (guild.LegacyPremiumExpiry != null)
                {
                    if (guild.LegacyPremiumExpiry.Value > DateTime.UtcNow)
                    {
                        await Context.SimpleEmbedAsync($"Expires on: {guild.LegacyPremiumExpiry.Value.ToString("dd MMM yyyy")} {guild.LegacyPremiumExpiry.Value.ToShortTimeString()}\nRemaining: {(guild.LegacyPremiumExpiry.Value - DateTime.UtcNow).GetReadableLength()}", Color.Blue);
                    }
                    else
                    {
                        await Context.SimpleEmbedAsync("Legacy premium has already expired.", Color.Red);
                    }
                }
                else
                {
                    await Context.SimpleEmbedAsync("This server does not have a legacy premium subscription.", Color.Red);
                }
            }
        }

        [Command("RegistrationLimit", RunMode = RunMode.Async)]
        [Summary("Displays the maximum amount of registrations for the server")]
        public virtual async Task GetRegisterLimit()
        {
            await Context.SimpleEmbedAsync($"Current registration limit is a maximum of: {Premium.GetRegistrationLimit(Context.Guild.Id)}", Color.Blue);
        }

        [Command("CompetitionInfo", RunMode = RunMode.Async)]
        [Alias("CompetitionSettings")]
        [Summary("Displays information about the current servers competition settings")]
        [RateLimit(1, 1, Measure.Minutes, RateLimitFlags.ApplyPerGuild)]
        public virtual async Task CompetitionInfo()
        {
            using (var db = new Database())
            {
                var comp = db.GetOrCreateCompetition(Context.Guild.Id);
                var embed = new EmbedBuilder
                {
                    Color = Color.Blue
                };

                var registered = ((IQueryable<Player>)db.Players).Count(x => x.GuildId == Context.Guild.Id);
                var gameCount = ((IQueryable<GameResult>)db.GameResults).Count(x => x.GuildId == Context.Guild.Id);
                var decGameCount = ((IQueryable<GameResult>)db.GameResults).Count(x => x.GuildId == Context.Guild.Id && (x.GameState == GameState.Decided || x.GameState == GameState.Draw));
                var manualGameCount = ((IQueryable<ManualGameResult>)db.ManualGameResults).Count(x => x.GuildId == Context.Guild.Id);

                embed.AddField("Roles",
                            $"**Register Role:** {(comp.RegisteredRankId == null ? "N/A" : MentionUtils.MentionRole(comp.RegisteredRankId.Value))}\n" +
                            $"**Admin Role:** {(comp.AdminRole == null ? "N/A" : MentionUtils.MentionRole(comp.AdminRole.Value))}\n" +
                            $"**Moderator Role:** {(comp.ModeratorRole == null ? "N/A" : MentionUtils.MentionRole(comp.ModeratorRole.Value))}");
                embed.AddField("Options",
                            $"**Allow Multi-Queuing:** {comp.AllowMultiQueueing}\n" +
                            $"**Allow Negative Score:** {comp.AllowNegativeScore}\n" +
                            $"**Update Nicknames:** {comp.UpdateNames}\n" +
                            $"**Display Error Messages:** {comp.DisplayErrors}\n" +
                            $"**Allow Self Rename:** {comp.AllowSelfRename}\n" +
                            $"**Allow Re-registering:** {comp.AllowReRegister}\n" +
                            $"**Requeue Delay:** {(comp.RequeueDelay.HasValue ? comp.RequeueDelay.Value.GetReadableLength() : "None")}\n" +
                            $"**Voting Enabled:** {comp.AllowVoting}\n" +
                            //$"**Custom Prefix:** {comp.Prefix ?? "N/A"}\n" +
                            $"**Command Prefix:** {comp.Prefix ?? Program.Prefix + "(default)"}\n" +
                            $"**Auto Queue Timeout:** {(comp.QueueTimeout.HasValue ? comp.QueueTimeout.Value.GetReadableLength() : "None")}");

                embed.AddField("Premium",
                            $"**Premium Buffer Until:** " +
                            $"{(comp.PremiumBuffer.HasValue ? comp.PremiumBuffer.Value.ToShortDateString() + " " + comp.PremiumBuffer.Value.ToShortTimeString() : "N/A")}\n" +
                            $"**Premium Buffer Count:** {(comp.BufferedPremiumCount.HasValue ? comp.BufferedPremiumCount.Value.ToString() : "N/A")}\n" +
                            $"**Premium Redeemer:** {(comp.PremiumRedeemer.HasValue ? MentionUtils.MentionUser(comp.PremiumRedeemer.Value) : "N/A")}\n" +
                            $"**Registration Limit:** {Premium.GetRegistrationLimit(Context.Guild.Id)}");

                embed.AddField("Stats",
                    $"**Registrations:** {registered}\n" +
                    $"**Games Created:** {gameCount}\n" +
                    $"**Games Submitted:** {decGameCount}\n" +
                    $"**Manual Games:** {manualGameCount}");

                embed.AddField("Formatting", 
                            $"**Nickname Format:** {comp.NameFormat}\n" +
                            $"**Registration Message:** {comp.RegisterMessageTemplate.FixLength(128)}");

                embed.AddField("Rank Info",
                $"**Default Win Amount:** +{comp.DefaultWinModifier}\n" +
                $"**Default Loss Amount:** -{comp.DefaultLossModifier}\n" +
                $"**Default Points On Register:** {comp.DefaultRegisterScore}\n" +
                $"For rank info use the `ranks` command");
                await ReplyAsync(null, false, embed.Build());
            }
        }

        [Command("SetRegisterScore", RunMode = RunMode.Sync)]
        [Summary("Sets default points when registering")]
        public virtual async Task SetRegisterRole(int amount = 0)
        {
            using (var db = new Database())
            {
                var competition = db.GetOrCreateCompetition(Context.Guild.Id);
                competition.DefaultRegisterScore = amount;
                db.Update(competition);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"When users register they will start with {amount} points", Color.Green);
            }
        }

        [Command("SetRegisterRole", RunMode = RunMode.Sync)]
        [Alias("Set RegisterRole", "RegisterRole")]
        [Summary("Sets or displays the current register role")]
        public virtual async Task SetRegisterRole([Remainder] IRole role = null)
        {
            using (var db = new Database())
            {
                var competition = db.GetOrCreateCompetition(Context.Guild.Id);
                if (role == null)
                {
                    if (competition.RegisteredRankId != null)
                    {
                        var gRole = Context.Guild.GetRole(competition.RegisteredRankId.Value);
                        if (gRole == null)
                        {
                            //Rank previously set but can no longer be found (deleted)
                            //May as well reset it.
                            competition.RegisteredRankId = null;
                            db.Update(competition);
                            db.SaveChanges();
                            await Context.SimpleEmbedAsync("Register role had previously been set but can no longer be found in the server. It has been reset.", Color.DarkBlue);
                        }
                        else
                        {
                            await Context.SimpleEmbedAsync($"Current register role is: {gRole.Mention}", Color.Blue);
                        }
                    }
                    else
                    {
                        //var serverPrefix = Prefix.GetPrefix(Context.Guild.Id) ?? Prefix.DefaultPrefix;
                        await Context.SimpleEmbedAsync($"There is no register role set. You can set one with `SetRegisterRole @role` or `SetRegisterRole rolename`", Color.Blue);
                    }

                    return;
                }

                competition.RegisteredRankId = role.Id;
                db.Update(competition);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"Register role set to {role.Mention}", Color.Green);
            }
        }

        [Command("SetRegisterMessage", RunMode = RunMode.Sync)]
        [Alias("Set RegisterMessage")]
        [Summary("Sets the message shown to users when they register")]
        public virtual async Task SetRegisterMessageAsync([Remainder] string message = null)
        {
            using (var db = new Database())
            {
                if (message == null)
                {
                    message = "You have registered as `{name}`, all roles/name updates have been applied if applicable.";
                }
                var competition = db.GetOrCreateCompetition(Context.Guild.Id);
                competition.RegisterMessageTemplate = message;
                var testProfile = new Player(0, 0, "Player");
                testProfile.Wins = 5;
                testProfile.Losses = 2;
                testProfile.Draws = 1;
                testProfile.Points = 600;
                var exampleRegisterMessage = competition.FormatRegisterMessage(testProfile);

                db.Update(competition);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"Register Message set.\nExample:\n{exampleRegisterMessage}", Color.Green);
            }
        }

        [Command("RegisterMessage", RunMode = RunMode.Async)]
        [Summary("Displays the current register message for the server")]
        public virtual async Task ShowRegisterMessageAsync()
        {
            using (var db = new Database())
            {
                var competition = db.GetOrCreateCompetition(Context.Guild.Id);
                var testProfile = new Player(0, 0, "Player");
                testProfile.Wins = 5;
                testProfile.Losses = 2;
                testProfile.Draws = 1;
                testProfile.Points = 600;

                db.Update(competition);
                db.SaveChanges();
                var response = new EmbedBuilder
                {
                    Color = Color.Blue
                };

                if (!string.IsNullOrWhiteSpace(competition.RegisterMessageTemplate))
                {
                    response.AddField("Unformatted Message", competition.RegisterMessageTemplate);
                    response.AddField("Example Message", competition.FormatRegisterMessage(testProfile));
                    await ReplyAsync(null, false, response.Build());
                    return;
                }

                await Context.SimpleEmbedAsync($"This server does not have a register message set.", Color.DarkBlue);
            }
        }

        [Command("RegisterMessageFormats", RunMode = RunMode.Async)]
        [Alias("RegisterFormats")]
        [Summary("Shows replacements that can be used in the register message")]
        public virtual async Task ShowRegistrationFormatsAsync()
        {
            var response = "**Register Message Formats**\n" + // Use Title
                "{score} - Total points\n" +
                "{name} - Registration name\n" +
                "{wins} - Total wins\n" +
                "{draws} - Total draws\n" +
                "{losses} - Total losses\n" +
                "{games} - Games played\n\n" +
                "Example:\n" +
                "`SetRegisterMessage Thank you for registering {name}` => `Thank you for registering Player`\n" +
                "NOTE: Format is limited to 1024 characters long";

            await Context.SimpleEmbedAsync(response, Color.Blue);
        }

        [Command("SetNicknameFormat", RunMode = RunMode.Sync)]
        [Alias("Set NicknameFormat", "NicknameFormat", "NameFormat", "SetNameFormat")]
        [Summary("Sets how user nicknames are formatted")]
        public virtual async Task SetNicknameFormatAsync([Remainder] string format)
        {
            using (var db = new Database())
            {
                var competition = db.GetOrCreateCompetition(Context.Guild.Id);
                competition.NameFormat = format;
                var testProfile = new Player(0, 0, "Player");
                testProfile.Wins = 5;
                testProfile.Losses = 2;
                testProfile.Draws = 1;
                testProfile.Points = 600;
                var exampleNick = competition.GetNickname(testProfile);

                db.Update(competition);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"Nickname Format set.\nExample: `{exampleNick}`", Color.Green);
            }
        }

        [Command("NicknameFormats", RunMode = RunMode.Async)]
        [Alias("NameFormats")]
        [Summary("Shows replacements that can be used in the user nickname formats")]
        public virtual async Task ShowNicknameFormatsAsync()
        {
            var response = "**NickNameFormats**\n" + // Use Title
                "{score} - Total points\n" +
                "{name} - Registration name\n" +
                "{wins} - Total wins\n" +
                "{draws} - Total draws\n" +
                "{losses} - Total losses\n" +
                "{games} - Games played\n\n" +
                "Examples:\n" +
                "`SetNicknameFormat {score} - {name}` `1000 - Player`\n" +
                "`SetNicknameFormat [{wins}] {name}` `[5] Player`\n" +
                "NOTE: Nicknames are limited to 32 characters long on discord";

            await Context.SimpleEmbedAsync(response, Color.Blue);
        }

        [Command("AddRank", RunMode = RunMode.Sync)]
        [Alias("Add Rank", "UpdateRank")]
        [Summary("Adds a new rank with the specified amount of points")]
        public virtual async Task AddRank(IRole role, int points)
        {
            using (var db = new Database())
            {
                var oldRank = db.Ranks.Find(role.Id);
                if (oldRank != null)
                {
                    oldRank.Points = points;
                    db.Ranks.Update(oldRank);
                }
                else
                {
                    var newRank = new Rank
                    {
                        RoleId = role.Id,
                        GuildId = Context.Guild.Id,
                        Points = points
                    };
                    db.Ranks.Add(newRank);
                }

                db.SaveChanges();
                await Context.SimpleEmbedAsync($"Rank {(oldRank != null ? "updated" : "added")}, if you wish to change the win/loss point values, use the `RankWinModifier` and `RankLossModifier` commands.", Color.Green);
            }
        }

        [Priority(1)]
        [Command("AddRank", RunMode = RunMode.Sync)]
        [Alias("Add Rank", "UpdateRank")]
        [Summary("Adds a new rank with the specified amount of points and win/loss modifiers")]
        public virtual async Task AddRank(IRole role, int points, int win, int lose)
        {
            using (var db = new Database())
            {
                var oldRank = db.Ranks.Find(role.Id);
                if (oldRank != null)
                {
                    oldRank.Points = points;
                    oldRank.WinModifier = win;
                    oldRank.LossModifier = lose;
                    db.Ranks.Update(oldRank);
                }
                else
                {
                    var newRank = new Rank
                    {
                        RoleId = role.Id,
                        GuildId = Context.Guild.Id,
                        Points = points,
                        WinModifier = win,
                        LossModifier = lose
                    };
                    db.Ranks.Add(newRank);
                }
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"Rank {(oldRank != null ? "updated" : "added")}.\n**Required Points:** {points}\n**Win Modifier:** +{win}\n**Loss Modifier:** -{lose}", Color.Green);
            }
        }

        [Command("AddRank", RunMode = RunMode.Sync)]
        [Alias("Add Rank", "UpdateRank")]
        [Summary("Adds a new rank with the specified amount of points and win/loss modifiers")]
        public virtual async Task AddRank(int points, IRole role, int win, int lose)
        {
            await AddRank(role, points, win, lose);
        }

        [Command("AddRank", RunMode = RunMode.Sync)]
        [Alias("Add Rank", "UpdateRank")]
        [Summary("Adds a new rank with the specified amount of points")]
        public virtual async Task AddRank(int points, IRole role)
        {
            await AddRank(role, points);
        }

        [Command("PurgeRanks", RunMode = RunMode.Sync)]
        [Summary("Remove all ranks that no longer have a role")]
        public virtual async Task RemoveRank()
        {
            using (var db = new Database())
            {
                var ranks = db.Ranks.AsQueryable().Where(x => x.GuildId == Context.Guild.Id).ToArray();
                var guildRoleIds = Context.Guild.Roles.Select(x => x.Id).ToArray();
                var removed = ranks.Where(x => !guildRoleIds.Contains(x.RoleId)).ToArray();
                db.Ranks.RemoveRange(removed);
                db.SaveChanges();
                await Context.SimpleEmbedAsync("Ranks Removed.", Color.Green);
            }
        }

        [Command("RemoveRank", RunMode = RunMode.Sync)]
        [Alias("Remove Rank", "DelRank")]
        [Summary("Removes a rank based of the role's id")]
        public virtual async Task RemoveRank(ulong roleId)
        {
            using (var db = new Database())
            {
                var rank = db.Ranks.Find(roleId);
                if (rank == null)
                {
                    await Context.SimpleEmbedAsync("Invalid Rank.", Color.Red);
                    return;
                }

                db.Ranks.Remove(rank);
                db.SaveChanges();
                await Context.SimpleEmbedAsync("Rank Removed.", Color.Green);
            }
        }

        [Command("RemoveRank", RunMode = RunMode.Sync)]
        [Alias("Remove Rank", "DelRank")]
        [Summary("Removes a rank")]
        public virtual async Task RemoveRank(IRole role)
        {
            await RemoveRank(role.Id);
        }

        [Command("AllowNegativeScore", RunMode = RunMode.Sync)]
        [Alias("AllowNegative")]
        [Summary("Sets whether negative scores are allowed")]
        public virtual async Task AllowNegativeAsync(bool? allowNegative = null)
        {
            using (var db = new Database())
            {
                var competition = db.GetOrCreateCompetition(Context.Guild.Id);
                if (allowNegative == null)
                {
                    await Context.SimpleEmbedAsync($"Current Allow Negative Score Setting: {competition.AllowNegativeScore}", Color.Blue);
                    return;
                }
                competition.AllowNegativeScore = allowNegative.Value;
                db.Update(competition);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"Allow Negative Score set to {allowNegative.Value}", Color.Green);
            }
        }

        [Command("AllowMultiQueuing", RunMode = RunMode.Sync)]
        [Alias("AllowMultiQueueing", "AllowMulti-Queuing", "AllowMulti-Queuing", "AllowMultiQ", "AllowMultiQing")]
        [Summary("Sets whether users are allowed to join multiple queues at once")]
        public virtual async Task AllowMultiQueueingAsync(bool? allowMulti = null)
        {
            using (var db = new Database())
            {
                var competition = db.GetOrCreateCompetition(Context.Guild.Id);
                if (allowMulti == null)
                {
                    await Context.SimpleEmbedAsync($"Current Allow Multi-Queuing Setting: {competition.AllowMultiQueueing}", Color.Blue);
                    return;
                }
                competition.AllowMultiQueueing = allowMulti.Value;
                db.Update(competition);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"Allow Multi-Queuing set to {allowMulti.Value}", Color.Green);
            }
        }

        [Command("AllowReRegister", RunMode = RunMode.Sync)]
        [Summary("Sets whether users are allowed to run the register command multiple times")]
        public virtual async Task AllowReRegisterAsync(bool? reRegister = null)
        {
            using (var db = new Database())
            {
                var competition = db.GetOrCreateCompetition(Context.Guild.Id);
                if (reRegister == null)
                {
                    await Context.SimpleEmbedAsync($"Current Allow re-register Setting: {competition.AllowReRegister}", Color.Blue);
                    return;
                }
                competition.AllowReRegister = reRegister.Value;
                db.Update(competition);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"Allow re-register set to {reRegister.Value}", Color.Green);
            }
        }

        [Command("AllowSelfRename", RunMode = RunMode.Sync)]
        [Alias("AllowRename")]
        [Summary("Sets whether users are allowed to use the rename command")]
        public virtual async Task AllowSelfRenameAsync(bool? selfRename = null)
        {
            using (var db = new Database())
            {
                var competition = db.GetOrCreateCompetition(Context.Guild.Id);
                if (selfRename == null)
                {
                    await Context.SimpleEmbedAsync($"Current Allow Self Rename Setting: {competition.AllowSelfRename}", Color.Blue);
                    return;
                }
                competition.AllowSelfRename = selfRename.Value;
                db.Update(competition);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"Allow Self Rename set to {selfRename.Value}", Color.Green);
            }
        }

        [Command("DisplayErrors", RunMode = RunMode.Sync)]
        [Alias("ShowErrors")]
        [Summary("Sets whether error messages are displayed to users")]
        public virtual async Task DisplayErrorsAsync(bool? displayErrors = null)
        {
            using (var db = new Database())
            {
                var competition = db.GetOrCreateCompetition(Context.Guild.Id);
                if (displayErrors == null)
                {
                    await Context.SimpleEmbedAsync($"Current DisplayErrors Setting: {competition.DisplayErrors}", Color.Blue);
                    return;
                }
                competition.DisplayErrors = displayErrors.Value;
                db.Update(competition);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"Display Errors set to {displayErrors.Value}", Color.Green);
            }
        }

        [Command("PurgeRegistrations", RunMode = RunMode.Sync)]
        [Summary("Removes registrations from users who are no longer in the server.")]
        public virtual async Task PurgeRegistrationsAsync(string confirm = null)
        {
            string confirmKey = "erkjbg4rt";
            if (confirm == null || !confirm.Equals(confirmKey, StringComparison.OrdinalIgnoreCase))
            {
                await Context.SimpleEmbedAsync($"Please __re-run this command__ with confirmation code `{confirmKey}`\n" +
                                       $"`PurgeRegistrations {confirmKey}`\n\nThis command will only remove registrations from users who are no longer in the server.", Color.Blue);
                return;
            }

            using (var db = new Database())
            {
                var players = db.Players.AsQueryable().Where(x => x.GuildId == Context.Guild.Id).ToArray();
                var missing = players.Where(x => Context.Guild.GetUser(x.UserId) == null).ToArray();
                db.Players.RemoveRange(missing);
                db.SaveChanges();
                Extensions.Extensions.RegCache.Clear();

                await Context.SimpleEmbedAsync($"Removed {missing.Length} registrations.", Color.Green);
            }
        }

        [Command("DefaultWinModifier", RunMode = RunMode.Sync)]
        [Alias("SetDefaultWinModifier")]
        [Summary("Sets the default amount of points users gain when winning a game.")]
        public virtual async Task CompWinModifier(int? amountToAdd = null)
        {
            using (var db = new Database())
            {
                var competition = db.GetOrCreateCompetition(Context.Guild.Id);

                if (!amountToAdd.HasValue)
                {
                    await Context.SimpleEmbedAsync($"Current DefaultWinModifier Setting: {competition.DefaultWinModifier}", Color.Blue);
                    return;
                }
                competition.DefaultWinModifier = amountToAdd.Value;
                db.Update(competition);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"DefaultWinModifier set to {competition.DefaultWinModifier}", Color.Green);
            }
        }

        [Command("DefaultLossModifier", RunMode = RunMode.Sync)]
        [Alias("SetDefaultLossModifier")]
        [Summary("Sets the default amount of points users lose when losing a game.")]
        public virtual async Task CompLossModifier(int? amountToSubtract = null)
        {
            using (var db = new Database())
            {
                var competition = db.GetOrCreateCompetition(Context.Guild.Id);

                if (!amountToSubtract.HasValue)
                {
                    await Context.SimpleEmbedAsync($"Current DefaultLossModifier Setting: {competition.DefaultLossModifier}", Color.Blue);
                    return;
                }
                competition.DefaultLossModifier = amountToSubtract.Value;
                db.Update(competition);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"DefaultLossModifier set to {competition.DefaultLossModifier}", Color.Green);
            }
        }

        [Command("RankLossModifier", RunMode = RunMode.Sync)]
        [Alias("SetRankLossModifier")]
        [Summary("Sets the amount of points lost for a user with the specified rank.")]
        public virtual async Task RankLossModifier(IRole role, int? amountToSubtract = null)
        {
            using (var db = new Database())
            {
                var rank = db.Ranks.Find(role.Id);
                if (rank == null)
                {
                    await Context.SimpleEmbedAsync("Provided role is not a rank.", Color.Red);
                    return;
                }

                rank.LossModifier = amountToSubtract;
                db.Update(rank);
                db.SaveChanges();
                if (!amountToSubtract.HasValue)
                {
                    var competition = db.GetOrCreateCompetition(Context.Guild.Id);
                    await Context.SimpleEmbedAsync($"This rank will now use the server's default loss value (-{competition.DefaultLossModifier}) when subtracting points.", Color.Blue);
                }
                else
                {
                    await Context.SimpleEmbedAsync($"When a player with this rank loses they will lose {amountToSubtract} points", Color.Green);
                }
            }
        }

        [Command("RankWinModifier", RunMode = RunMode.Sync)]
        [Alias("SetRankWinModifier")]
        [Summary("Sets the amount of points lost for a user with the specified rank.")]
        public virtual async Task RankWinModifier(IRole role, int? amountToAdd = null)
        {
            using (var db = new Database())
            {
                var rank = db.Ranks.Find(role.Id);
                if (rank == null)
                {
                    await Context.SimpleEmbedAsync("Provided role is not a rank.", Color.Red);
                    return;
                }

                rank.WinModifier = amountToAdd;
                db.Update(rank);
                db.SaveChanges();
                if (!amountToAdd.HasValue)
                {
                    var competition = db.GetOrCreateCompetition(Context.Guild.Id);
                    await Context.SimpleEmbedAsync($"This rank will now use the server's default win value (+{competition.DefaultWinModifier}) whenSimpleEmbedAsync adding points.", Color.Blue);
                }
                else
                {
                    await Context.SimpleEmbedAsync($"When a player with this rank wins they will gain {amountToAdd} points", Color.Green);
                }
            }
        }

        [Command("UpdateNicknames", RunMode = RunMode.Sync)]
        [Summary("Sets whether the bot will update user nicknames.")]
        public virtual async Task UpdateNicknames(bool? updateNicknames = null)
        {
            using (var db = new Database())
            {
                var competition = db.GetOrCreateCompetition(Context.Guild.Id);
                if (updateNicknames == null)
                {
                    await Context.SimpleEmbedAsync($"Current Update Nicknames Setting: {competition.UpdateNames}", Color.Blue);
                    return;
                }
                competition.UpdateNames = updateNicknames.Value;
                db.Update(competition);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"Update Nicknames set to {competition.UpdateNames}", Color.Green);
            }
        }

        [Command("AllowVoting", RunMode = RunMode.Sync)]
        [Summary("Sets whether users are allowed to vote on the result of games.")]
        public virtual async Task AllowVotingAsync(bool? allowVoting = null)
        {
            using (var db = new Database())
            {
                var competition = db.GetOrCreateCompetition(Context.Guild.Id);
                if (allowVoting == null)
                {
                    await Context.SimpleEmbedAsync($"Current Allow Voting Setting: {competition.AllowVoting}", Color.Blue);
                    return;
                }
                competition.AllowVoting = allowVoting.Value;
                db.Update(competition);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"Allow voting set to {competition.AllowVoting}", Color.Green);
            }
        }

        /*[Command("CreateReactionRegistration", RunMode = RunMode.Sync)]
        [Summary("Creates a message which users can react to in order to register")]
        public virtual async Task CreateReactAsync([Remainder]string message = null)
        {
            using (var db = new Database())
            {
                var competition = db.GetOrCreateCompetition(Context.Guild.Id);

                var response = await Context.SimpleEmbedAsync(message);
                competition.ReactiveMessage = response.Id;
                db.Update(competition);
                db.SaveChanges();
                await response.AddReactionAsync(ReactiveMessageService.registrationConfirmEmoji);
            }
        }*/

        [Command("ReQueueDelay", RunMode = RunMode.Sync)]
        [Summary("Set or displays the amount of time required between joining queues.")]
        [Alias("SetRequeueDelay")]
        public virtual async Task SetReQueueDelayAsync([Remainder]TimeSpan? delay = null)
        {
            using (var db = new Database())
            {
                var competition = db.GetOrCreateCompetition(Context.Guild.Id);
                if (delay == null)
                {
                    await Context.SimpleEmbedAsync($"Current Requeue Delay Setting: {(competition.RequeueDelay.HasValue ? competition.RequeueDelay.Value.GetReadableLength() : "None")}", Color.Blue);
                    return;
                }

                competition.RequeueDelay = delay;
                db.Update(competition);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"Requeue Delay Set to {competition.RequeueDelay.Value.GetReadableLength()}", Color.Green);
            }
        }

        [Command("ResetReQueueDelay", RunMode = RunMode.Sync)]
        [Summary("Removes the amount of time required between joining queues.")]
        public virtual async Task ResetReQueueDelayAsync()
        {
            using (var db = new Database())
            {
                var competition = db.GetOrCreateCompetition(Context.Guild.Id);

                competition.RequeueDelay = null;
                db.Update(competition);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"Requeue Delay Removed.", Color.Green);
            }
        }

        [Command("SetQueueTimeout", RunMode = RunMode.Sync)]
        [Summary("Set an automated queue timeout value.")]
        public virtual async Task SetQueueTimeout(TimeSpan? timeout = null)
        {
            if (timeout.HasValue && timeout < TimeSpan.FromMinutes(10))
            {
                await Context.SimpleEmbedAsync("Minimum timeout length is 10 minutes.");
                return;
            }

            if (!Premium.IsPremium(Context.Guild.Id))
            {
                await Context.SimpleEmbedAsync($"This feature is for premium ELO servers only. {Premium.PremiumConfig.ServerInvite}");
                return;
            }

            using (var db = new Database())
            {
                var competition = db.GetOrCreateCompetition(Context.Guild.Id);
                if (timeout == null)
                {
                    await Context.SimpleEmbedAsync($"Current Queue Timeout Setting: {(competition.QueueTimeout.HasValue ? competition.QueueTimeout.Value.GetReadableLength() : "None")}", Color.Blue);
                    return;
                }

                competition.QueueTimeout = timeout;
                db.Update(competition);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"Queue Timeout Set to {competition.QueueTimeout.Value.GetReadableLength()}", Color.Green);
            }
        }

        [Command("ResetQueueTimeout", RunMode = RunMode.Sync)]
        [Summary("Remove the queue timeout.")]
        public virtual async Task ResetQueueTimeout()
        {
            using (var db = new Database())
            {
                var competition = db.GetOrCreateCompetition(Context.Guild.Id);
                competition.QueueTimeout = null;
                db.Update(competition);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"Queue Timeout Removed.", Color.Green);
            }
        }
    }
}
