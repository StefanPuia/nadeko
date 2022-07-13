#nullable disable
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Db;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Utility.Patronage;
using NadekoBot.Modules.Gambling.Bank;
using NadekoBot.Modules.Gambling.Common;
using NadekoBot.Modules.Gambling.Services;
using NadekoBot.Modules.Utility.Services;
using NadekoBot.Services.Currency;
using NadekoBot.Services.Database.Models;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Nadeko.Econ.Gambling.Rps;

namespace NadekoBot.Modules.Gambling;

public partial class Gambling : GamblingModule<GamblingService>
{
    private readonly IGamblingService _gs;
    private readonly DbService _db;
    private readonly ICurrencyService _cs;
    private readonly DiscordSocketClient _client;
    private readonly NumberFormatInfo _enUsCulture;
    private readonly DownloadTracker _tracker;
    private readonly GamblingConfigService _configService;
    private readonly IBankService _bank;
    private readonly IPatronageService _ps;
    private readonly RemindService _remind;

    private IUserMessage rdMsg;

    public Gambling(
        IGamblingService gs,
        DbService db,
        ICurrencyService currency,
        DiscordSocketClient client,
        DownloadTracker tracker,
        GamblingConfigService configService,
        IBankService bank,
        IPatronageService ps,
        RemindService remind)
        : base(configService)
    {
        _gs = gs;
        _db = db;
        _cs = currency;
        _client = client;
        _bank = bank;
        _ps = ps;
        _remind = remind;

        _enUsCulture = new CultureInfo("en-US", false).NumberFormat;
        _enUsCulture.NumberDecimalDigits = 0;
        _enUsCulture.NumberGroupSeparator = " ";
        _tracker = tracker;
        _configService = configService;
    }

    public async Task<string> GetBalanceStringAsync(ulong userId)
    {
        var bal = await _cs.GetBalanceAsync(userId);
        return N(bal);
    }

    [Cmd]
    public async partial Task Economy()
    {
        var ec = await _service.GetEconomyAsync();
        decimal onePercent = 0;

        // This stops the top 1% from owning more than 100% of the money
        if (ec.Cash > 0)
        {
            onePercent = ec.OnePercent / (ec.Cash - ec.Bot);
        }

        // [21:03] Bob Page: Kinda remids me of US economy
        var embed = _eb.Create()
                       .WithTitle(GetText(strs.economy_state))
                       .AddField(GetText(strs.currency_owned), N(ec.Cash - ec.Bot))
                       .AddField(GetText(strs.currency_one_percent), (onePercent * 100).ToString("F2") + "%")
                       .AddField(GetText(strs.currency_planted), N(ec.Planted))
                       .AddField(GetText(strs.owned_waifus_total), N(ec.Waifus))
                       .AddField(GetText(strs.bot_currency), N(ec.Bot))
                       .AddField(GetText(strs.bank_accounts), N(ec.Bank))
                       .AddField(GetText(strs.total), N(ec.Cash + ec.Planted + ec.Waifus + ec.Bank))
                       .WithOkColor();

        // ec.Cash already contains ec.Bot as it's the total of all values in the CurrencyAmount column of the DiscordUser table
        await ctx.Channel.EmbedAsync(embed);
    }

    private static readonly FeatureLimitKey _timelyKey = new FeatureLimitKey()
    {
        Key = "timely:extra_percent",
        PrettyName = "Timely"
    };

    public class RemindMeInteraction : NInteraction
    {
        public RemindMeInteraction(
            [NotNull] DiscordSocketClient client,
            ulong userId,
            [NotNull] Func<SocketMessageComponent, Task> action)
            : base(client, userId, action)
        {
        }

        protected override NadekoInteractionData Data
            => new NadekoInteractionData(
                Emote: Emoji.Parse("⏰"),
                CustomId: "timely:remind_me",
                Text: "Remind me"
            );
    }

    private Func<SocketMessageComponent, Task> RemindTimelyAction(DateTime when)
        => async smc =>
        {
            var tt = TimestampTag.FromDateTime(when, TimestampTagStyles.Relative);

            await _remind.AddReminderAsync(ctx.User.Id,
                ctx.User.Id,
                ctx.Guild.Id,
                true,
                when,
                GetText(strs.timely_time));

            await smc.RespondConfirmAsync(_eb, GetText(strs.remind_timely(tt)), ephemeral: true);
        };

    [Cmd]
    public async partial Task Timely()
    {
        var val = Config.Timely.Amount;
        var period = Config.Timely.Cooldown;
        if (val <= 0 || period <= 0)
        {
            await ReplyErrorLocalizedAsync(strs.timely_none);
            return;
        }

        if (await _service.ClaimTimelyAsync(ctx.User.Id, period) is { } rem)
        {
            var now = DateTime.UtcNow;
            var relativeTag = TimestampTag.FromDateTime(now.Add(rem), TimestampTagStyles.Relative);
            await ReplyErrorLocalizedAsync(strs.timely_already_claimed(relativeTag));
            return;
        }

        var result = await _ps.TryGetFeatureLimitAsync(_timelyKey, ctx.User.Id, 0);

        val = (int)(val * (1 + (result.Quota! * 0.01f)));

        await _cs.AddAsync(ctx.User.Id, val, new("timely", "claim"));

        var inter = new RemindMeInteraction(_client,
            ctx.User.Id,
            RemindTimelyAction(DateTime.UtcNow.Add(TimeSpan.FromHours(period))));
        
        await ReplyConfirmLocalizedAsync(strs.timely(N(val), period), inter.GetInteraction());
    }

    [Cmd]
    [OwnerOnly]
    public async partial Task TimelyReset()
    {
        await _service.RemoveAllTimelyClaimsAsync();
        await ReplyConfirmLocalizedAsync(strs.timely_reset);
    }

    [Cmd]
    [OwnerOnly]
    public async partial Task TimelySet(int amount, int period = 24)
    {
        if (amount < 0 || period < 0)
        {
            return;
        }

        _configService.ModifyConfig(gs =>
        {
            gs.Timely.Amount = amount;
            gs.Timely.Cooldown = period;
        });

        if (amount == 0)
        {
            await ReplyConfirmLocalizedAsync(strs.timely_set_none);
        }
        else
        {
            await ReplyConfirmLocalizedAsync(strs.timely_set(Format.Bold(N(amount)), Format.Bold(period.ToString())));
        }
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async partial Task Raffle([Leftover] IRole role = null)
    {
        role ??= ctx.Guild.EveryoneRole;

        var members = (await role.GetMembersAsync()).Where(u => u.Status != UserStatus.Offline);
        var membersArray = members as IUser[] ?? members.ToArray();
        if (membersArray.Length == 0)
        {
            return;
        }

        var usr = membersArray[new NadekoRandom().Next(0, membersArray.Length)];
        await SendConfirmAsync("🎟 " + GetText(strs.raffled_user),
            $"**{usr.Username}#{usr.Discriminator}**",
            footer: $"ID: {usr.Id}");
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async partial Task RaffleAny([Leftover] IRole role = null)
    {
        role ??= ctx.Guild.EveryoneRole;

        var members = await role.GetMembersAsync();
        var membersArray = members as IUser[] ?? members.ToArray();
        if (membersArray.Length == 0)
        {
            return;
        }

        var usr = membersArray[new NadekoRandom().Next(0, membersArray.Length)];
        await SendConfirmAsync("🎟 " + GetText(strs.raffled_user),
            $"**{usr.Username}#{usr.Discriminator}**",
            footer: $"ID: {usr.Id}");
    }

    [Cmd]
    [Priority(2)]
    public partial Task CurrencyTransactions(int page = 1)
        => InternalCurrencyTransactions(ctx.User.Id, page);

    [Cmd]
    [OwnerOnly]
    [Priority(0)]
    public partial Task CurrencyTransactions([Leftover] IUser usr)
        => InternalCurrencyTransactions(usr.Id, 1);

    [Cmd]
    [OwnerOnly]
    [Priority(1)]
    public partial Task CurrencyTransactions(IUser usr, int page)
        => InternalCurrencyTransactions(usr.Id, page);

    private async Task InternalCurrencyTransactions(ulong userId, int page)
    {
        if (--page < 0)
        {
            return;
        }

        List<CurrencyTransaction> trs;
        await using (var uow = _db.GetDbContext())
        {
            trs = uow.CurrencyTransactions.GetPageFor(userId, page);
        }

        var embed = _eb.Create()
                       .WithTitle(GetText(strs.transactions(((SocketGuild)ctx.Guild)?.GetUser(userId)?.ToString()
                                                            ?? $"{userId}")))
                       .WithOkColor();

        var sb = new StringBuilder();
        foreach (var tr in trs)
        {
            var change = tr.Amount >= 0 ? "🔵" : "🔴";
            var kwumId = new kwum(tr.Id).ToString();
            var date = $"#{Format.Code(kwumId)} `〖{GetFormattedCurtrDate(tr)}〗`";

            sb.AppendLine($"\\{change} {date} {Format.Bold(N(tr.Amount))}");
            var transactionString = GetHumanReadableTransaction(tr.Type, tr.Extra, tr.OtherId);
            if (transactionString is not null)
            {
                sb.AppendLine(transactionString);
            }

            if (!string.IsNullOrWhiteSpace(tr.Note))
            {
                sb.AppendLine($"\t`Note:` {tr.Note.TrimTo(50)}");
            }
        }

        embed.WithDescription(sb.ToString());
        embed.WithFooter(GetText(strs.page(page + 1)));
        await ctx.Channel.EmbedAsync(embed);
    }

    private static string GetFormattedCurtrDate(CurrencyTransaction ct)
        => $"{ct.DateAdded:HH:mm yyyy-MM-dd}";

    [Cmd]
    public async partial Task CurrencyTransaction(kwum id)
    {
        int intId = id;
        await using var uow = _db.GetDbContext();

        var tr = await uow.CurrencyTransactions.ToLinqToDBTable()
                          .Where(x => x.Id == intId && x.UserId == ctx.User.Id)
                          .FirstOrDefaultAsync();

        if (tr is null)
        {
            await ReplyErrorLocalizedAsync(strs.not_found);
            return;
        }

        var eb = _eb.Create(ctx).WithOkColor();

        eb.WithAuthor(ctx.User);
        eb.WithTitle(GetText(strs.transaction));
        eb.WithDescription(new kwum(tr.Id).ToString());
        eb.AddField("Amount", N(tr.Amount));
        eb.AddField("Type", tr.Type, true);
        eb.AddField("Extra", tr.Extra, true);

        if (tr.OtherId is ulong other)
        {
            eb.AddField("From Id", other);
        }

        if (!string.IsNullOrWhiteSpace(tr.Note))
        {
            eb.AddField("Note", tr.Note);
        }

        eb.WithFooter(GetFormattedCurtrDate(tr));

        await ctx.Channel.EmbedAsync(eb);
    }

    private string GetHumanReadableTransaction(string type, string subType, ulong? maybeUserId)
        => (type, subType, maybeUserId) switch
        {
            ("gift", var name, ulong userId) => GetText(strs.curtr_gift(name, userId)),
            ("award", var name, ulong userId) => GetText(strs.curtr_award(name, userId)),
            ("take", var name, ulong userId) => GetText(strs.curtr_take(name, userId)),
            ("blackjack", _, _) => $"Blackjack - {subType}",
            ("wheel", _, _) => $"Wheel Of Fortune - {subType}",
            ("rps", _, _) => $"Rock Paper Scissors - {subType}",
            (null, _, _) => null,
            (_, null, _) => null,
            (_, _, ulong userId) => $"{type.Titleize()} - {subType.Titleize()} | [{userId}]",
            _ => $"{type.Titleize()} - {subType.Titleize()}"
        };
    
    [Cmd]
    [Priority(0)]
    public async partial Task Cash(ulong userId)
    {
        var cur = await GetBalanceStringAsync(userId);
        await ReplyConfirmLocalizedAsync(strs.has(Format.Code(userId.ToString()), cur));
    }

    private async Task BankAction(SocketMessageComponent smc)
    {
        var balance = await _bank.GetBalanceAsync(ctx.User.Id);

        await N(balance)
              .Pipe(strs.bank_balance)
              .Pipe(GetText)
              .Pipe(text => smc.RespondConfirmAsync(_eb, text, ephemeral: true));
    }

    private NadekoButtonInteraction CreateCashInteraction()
        => new CashInteraction(_client, ctx.User.Id, BankAction).GetInteraction();

    [Cmd]
    [Priority(1)]
    public async partial Task Cash([Leftover] IUser user = null)
    {
        user ??= ctx.User;
        var cur = await GetBalanceStringAsync(user.Id);
        
        var inter = user == ctx.User
            ? CreateCashInteraction()
            : null;

        await ConfirmLocalizedAsync(
            user.ToString()
                .Pipe(Format.Bold)
                .With(cur)
                .Pipe(strs.has),
            inter);
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [Priority(0)]
    public async partial Task Give(ShmartNumber amount, IGuildUser receiver, [Leftover] string msg)
    {
        if (amount <= 0 || ctx.User.Id == receiver.Id || receiver.IsBot)
        {
            return;
        }

        if (!await _cs.TransferAsync(_eb, ctx.User, receiver, amount, msg, N(amount)))
        {
            await ReplyErrorLocalizedAsync(strs.not_enough(CurrencySign));
            return;
        }

        await ReplyConfirmLocalizedAsync(strs.gifted(N(amount.Value), Format.Bold(receiver.ToString())));
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [Priority(1)]
    public partial Task Give(ShmartNumber amount, [Leftover] IGuildUser receiver)
        => Give(amount, receiver, null);

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [OwnerOnly]
    [Priority(0)]
    public partial Task Award(long amount, IGuildUser usr, [Leftover] string msg)
        => Award(amount, usr.Id, msg);

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [OwnerOnly]
    [Priority(1)]
    public partial Task Award(long amount, [Leftover] IGuildUser usr)
        => Award(amount, usr.Id);

    [Cmd]
    [OwnerOnly]
    [Priority(2)]
    public async partial Task Award(long amount, ulong usrId, [Leftover] string msg = null)
    {
        if (amount <= 0)
        {
            return;
        }

        var usr = await ((DiscordSocketClient)Context.Client).Rest.GetUserAsync(usrId);

        if (usr is null)
        {
            await ReplyErrorLocalizedAsync(strs.user_not_found);
            return;
        }

        await _cs.AddAsync(usr.Id, amount, new("award", ctx.User.ToString()!, msg, ctx.User.Id));
        await ReplyConfirmLocalizedAsync(strs.awarded(N(amount), $"<@{usrId}>"));
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [OwnerOnly]
    [Priority(3)]
    public async partial Task Award(long amount, [Leftover] IRole role)
    {
        var users = (await ctx.Guild.GetUsersAsync()).Where(u => u.GetRoles().Contains(role)).ToList();

        await _cs.AddBulkAsync(users.Select(x => x.Id).ToList(),
            amount,
            new("award", ctx.User.ToString()!, role.Name, ctx.User.Id));

        await ReplyConfirmLocalizedAsync(strs.mass_award(N(amount),
            Format.Bold(users.Count.ToString()),
            Format.Bold(role.Name)));
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [OwnerOnly]
    [Priority(0)]
    public async partial Task Take(long amount, [Leftover] IRole role)
    {
        var users = (await role.GetMembersAsync()).ToList();

        await _cs.RemoveBulkAsync(users.Select(x => x.Id).ToList(),
            amount,
            new("take", ctx.User.ToString()!, null, ctx.User.Id));

        await ReplyConfirmLocalizedAsync(strs.mass_take(N(amount),
            Format.Bold(users.Count.ToString()),
            Format.Bold(role.Name)));
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [OwnerOnly]
    [Priority(1)]
    public async partial Task Take(long amount, [Leftover] IGuildUser user)
    {
        if (amount <= 0)
        {
            return;
        }

        var extra = new TxData("take", ctx.User.ToString()!, null, ctx.User.Id);

        if (await _cs.RemoveAsync(user.Id, amount, extra))
        {
            await ReplyConfirmLocalizedAsync(strs.take(N(amount), Format.Bold(user.ToString())));
        }
        else
        {
            await ReplyErrorLocalizedAsync(strs.take_fail(N(amount), Format.Bold(user.ToString()), CurrencySign));
        }
    }

    [Cmd]
    [OwnerOnly]
    public async partial Task Take(long amount, [Leftover] ulong usrId)
    {
        if (amount <= 0)
        {
            return;
        }

        var extra = new TxData("take", ctx.User.ToString()!, null, ctx.User.Id);

        if (await _cs.RemoveAsync(usrId, amount, extra))
        {
            await ReplyConfirmLocalizedAsync(strs.take(N(amount), $"<@{usrId}>"));
        }
        else
        {
            await ReplyErrorLocalizedAsync(strs.take_fail(N(amount), Format.Code(usrId.ToString()), CurrencySign));
        }
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async partial Task RollDuel(IUser u)
    {
        if (ctx.User.Id == u.Id)
        {
            return;
        }

        //since the challenge is created by another user, we need to reverse the ids
        //if it gets removed, means challenge is accepted
        if (_service.Duels.TryRemove((ctx.User.Id, u.Id), out var game))
        {
            await game.StartGame();
        }
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async partial Task RollDuel(ShmartNumber amount, IUser u)
    {
        if (ctx.User.Id == u.Id)
        {
            return;
        }

        if (amount <= 0)
        {
            return;
        }

        var embed = _eb.Create().WithOkColor().WithTitle(GetText(strs.roll_duel));

        var description = string.Empty;

        var game = new RollDuelGame(_cs, _client.CurrentUser.Id, ctx.User.Id, u.Id, amount);
        //means challenge is just created
        if (_service.Duels.TryGetValue((ctx.User.Id, u.Id), out var other))
        {
            if (other.Amount != amount)
            {
                await ReplyErrorLocalizedAsync(strs.roll_duel_already_challenged);
            }
            else
            {
                await RollDuel(u);
            }

            return;
        }

        if (_service.Duels.TryAdd((u.Id, ctx.User.Id), game))
        {
            game.OnGameTick += GameOnGameTick;
            game.OnEnded += GameOnEnded;

            await ReplyConfirmLocalizedAsync(strs.roll_duel_challenge(Format.Bold(ctx.User.ToString()),
                Format.Bold(u.ToString()),
                Format.Bold(N(amount.Value))));
        }

        async Task GameOnGameTick(RollDuelGame arg)
        {
            var rolls = arg.Rolls.Last();
            description += $@"{Format.Bold(ctx.User.ToString())} rolled **{rolls.Item1}**
{Format.Bold(u.ToString())} rolled **{rolls.Item2}**
--
";
            embed = embed.WithDescription(description);

            if (rdMsg is null)
            {
                rdMsg = await ctx.Channel.EmbedAsync(embed);
            }
            else
            {
                await rdMsg.ModifyAsync(x => { x.Embed = embed.Build(); });
            }
        }

        async Task GameOnEnded(RollDuelGame rdGame, RollDuelGame.Reason reason)
        {
            try
            {
                if (reason == RollDuelGame.Reason.Normal)
                {
                    var winner = rdGame.Winner == rdGame.P1 ? ctx.User : u;
                    description += $"\n**{winner}** Won {N((long)(rdGame.Amount * 2 * 0.98))}";

                    embed = embed.WithDescription(description);

                    await rdMsg.ModifyAsync(x => x.Embed = embed.Build());
                }
                else if (reason == RollDuelGame.Reason.Timeout)
                {
                    await ReplyErrorLocalizedAsync(strs.roll_duel_timeout);
                }
                else if (reason == RollDuelGame.Reason.NoFunds)
                {
                    await ReplyErrorLocalizedAsync(strs.roll_duel_no_funds);
                }
            }
            finally
            {
                _service.Duels.TryRemove((u.Id, ctx.User.Id), out _);
            }
        }
    }

    [Cmd]
    public async partial Task BetRoll(ShmartNumber amount)
    {
        if (!await CheckBetMandatory(amount))
        {
            return;
        }

        var maybeResult = await _gs.BetRollAsync(ctx.User.Id, amount);
        if (!maybeResult.TryPickT0(out var result, out _))
        {
            await ReplyErrorLocalizedAsync(strs.not_enough(CurrencySign));
            return;
        }

        
        var win = (long)result.Won;
        string str;
        if (win > 0)
        {
            str = GetText(strs.br_win(N(win), result.Threshold + (result.Roll == 100 ? " 👑" : "")));
            await _cs.AddAsync(ctx.User, win, new("betroll", "win"));
        }
        else
        {
            str = GetText(strs.better_luck);
        }

        var eb = _eb.Create(ctx)
            .WithAuthor(ctx.User)
            .WithFooter(str)
            .AddField(GetText(strs.roll2), result.Roll.ToString(CultureInfo.InvariantCulture))
            .WithOkColor();

        await ctx.Channel.EmbedAsync(eb);
    }

    [Cmd]
    [NadekoOptions(typeof(LbOpts))]
    [Priority(0)]
    public partial Task Leaderboard(params string[] args)
        => Leaderboard(1, args);

    [Cmd]
    [NadekoOptions(typeof(LbOpts))]
    [Priority(1)]
    public async partial Task Leaderboard(int page = 1, params string[] args)
    {
        if (--page < 0)
        {
            return;
        }

        var (opts, _) = OptionsParser.ParseFrom(new LbOpts(), args);

        List<DiscordUser> cleanRichest;
        // it's pointless to have clean on dm context
        if (ctx.Guild is null)
        {
            opts.Clean = false;
        }

        if (opts.Clean)
        {
            await using (var uow = _db.GetDbContext())
            {
                cleanRichest = uow.DiscordUser.GetTopRichest(_client.CurrentUser.Id, 10_000);
            }

            await ctx.Channel.TriggerTypingAsync();
            await _tracker.EnsureUsersDownloadedAsync(ctx.Guild);

            var sg = (SocketGuild)ctx.Guild;
            cleanRichest = cleanRichest.Where(x => sg.GetUser(x.UserId) is not null).ToList();
        }
        else
        {
            await using var uow = _db.GetDbContext();
            cleanRichest = uow.DiscordUser.GetTopRichest(_client.CurrentUser.Id, 9, page).ToList();
        }

        await ctx.SendPaginatedConfirmAsync(page,
            curPage =>
            {
                var embed = _eb.Create().WithOkColor().WithTitle(CurrencySign + " " + GetText(strs.leaderboard));

                List<DiscordUser> toSend;
                if (!opts.Clean)
                {
                    using var uow = _db.GetDbContext();
                    toSend = uow.DiscordUser.GetTopRichest(_client.CurrentUser.Id, 9, curPage);
                }
                else
                {
                    toSend = cleanRichest.Skip(curPage * 9).Take(9).ToList();
                }

                if (!toSend.Any())
                {
                    embed.WithDescription(GetText(strs.no_user_on_this_page));
                    return embed;
                }

                for (var i = 0; i < toSend.Count; i++)
                {
                    var x = toSend[i];
                    var usrStr = x.ToString().TrimTo(20, true);

                    var j = i;
                    embed.AddField("#" + ((9 * curPage) + j + 1) + " " + usrStr, N(x.CurrencyAmount), true);
                }

                return embed;
            },
            opts.Clean ? cleanRichest.Count() : 9000,
            9,
            opts.Clean);
    }

    public enum InputRpsPick : byte
    {
        R = 0,
        Rock = 0,
        Rocket = 0,
        P = 1,
        Paper = 1,
        Paperclip = 1,
        S = 2,
        Scissors = 2
    }
    
    // todo check if trivia is being disposed
    [Cmd]
    public async partial Task Rps(InputRpsPick pick, ShmartNumber amount = default)
    {
        static string GetRpsPick(InputRpsPick p)
        {
            switch (p)
            {
                case InputRpsPick.R:
                    return "🚀";
                case InputRpsPick.P:
                    return "📎";
                default:
                    return "✂️";
            }
        }
        
        if (!await CheckBetOptional(amount) || amount == 1)
            return;

        var res = await _gs.RpsAsync(ctx.User.Id, amount, (byte)pick);

        if (!res.TryPickT0(out var result, out _))
        {
            await ReplyErrorLocalizedAsync(strs.not_enough(CurrencySign));
            return;
        }
        
        var embed = _eb.Create();
        
        string msg;
        if (result.Result == RpsResultType.Draw)
        {
            msg = GetText(strs.rps_draw(GetRpsPick(pick)));
        }
        else if (result.Result == RpsResultType.Win)
        {
            if((long)result.Won > 0)
                 embed.AddField(GetText(strs.won), N(amount.Value));

            msg = GetText(strs.rps_win(ctx.User.Mention,
                GetRpsPick(pick),
                GetRpsPick((InputRpsPick)result.ComputerPick)));
        }
        else
        {
            msg = GetText(strs.rps_win(ctx.Client.CurrentUser.Mention,
                GetRpsPick((InputRpsPick)result.ComputerPick),
                GetRpsPick(pick)));
        }

        embed
            .WithOkColor()
            .WithDescription(msg);

        await ctx.Channel.EmbedAsync(embed);
    }
    
    private static readonly ImmutableArray<string> _emojis =
        new[] { "⬆", "↖", "⬅", "↙", "⬇", "↘", "➡", "↗" }.ToImmutableArray();

    [Cmd]
    public async partial Task WheelOfFortune(ShmartNumber amount)
    {
        if (!await CheckBetMandatory(amount))
            return;

        var res = await _gs.WofAsync(ctx.User.Id, amount);
        if (!res.TryPickT0(out var result, out _))
        {
            await ReplyErrorLocalizedAsync(strs.not_enough(CurrencySign));
            return;
        }

        var wofMultipliers = Config.WheelOfFortune.Multipliers;
        await SendConfirmAsync(Format.Bold($@"{ctx.User} won: {N(result.Won)}

   『{wofMultipliers[1]}』   『{wofMultipliers[0]}』   『{wofMultipliers[7]}』

『{wofMultipliers[2]}』      {_emojis[result.Index]}      『{wofMultipliers[6]}』

     『{wofMultipliers[3]}』   『{wofMultipliers[4]}』   『{wofMultipliers[5]}』"));
    }
}