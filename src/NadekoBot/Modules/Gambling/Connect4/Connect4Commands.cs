﻿#nullable disable
using NadekoBot.Modules.Gambling.Common;
using NadekoBot.Modules.Gambling.Common.Connect4;
using NadekoBot.Modules.Gambling.Services;
using System.Text;

namespace NadekoBot.Modules.Gambling;

public partial class Gambling
{
    [Group]
    public partial class Connect4Commands : GamblingSubmodule<GamblingService>
    {
        private static readonly string[] numbers =
        {
            ":one:", ":two:", ":three:", ":four:", ":five:", ":six:", ":seven:", ":eight:"
        };

        private int RepostCounter
        {
            get => _repostCounter;
            set
            {
                if (value is < 0 or > 7)
                    _repostCounter = 0;
                else _repostCounter = value;
            }
        }

        private readonly DiscordSocketClient _client;
        private readonly ICurrencyService _cs;

        private IUserMessage msg;

        private int _repostCounter;

        public Connect4Commands(DiscordSocketClient client, ICurrencyService cs, GamblingConfigService gamb)
            : base(gamb)
        {
            _client = client;
            _cs = cs;
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [NadekoOptionsAttribute(typeof(Connect4Game.Options))]
        public async partial Task Connect4(params string[] args)
        {
            var (options, _) = OptionsParser.ParseFrom(new Connect4Game.Options(), args);
            if (!await CheckBetOptional(options.Bet))
                return;

            var newGame = new Connect4Game(ctx.User.Id, ctx.User.ToString(), options, _cs);
            Connect4Game game;
            if ((game = _service.Connect4Games.GetOrAdd(ctx.Channel.Id, newGame)) != newGame)
            {
                if (game.CurrentPhase != Connect4Game.Phase.Joining)
                    return;

                newGame.Dispose();
                //means game already exists, try to join
                var joined = await game.Join(ctx.User.Id, ctx.User.ToString(), options.Bet);
                return;
            }

            if (options.Bet > 0)
                if (!await _cs.RemoveAsync(ctx.User.Id, "Connect4-bet", options.Bet, true))
                {
                    await ReplyErrorLocalizedAsync(strs.not_enough(CurrencySign));
                    _service.Connect4Games.TryRemove(ctx.Channel.Id, out _);
                    game.Dispose();
                    return;
                }

            game.OnGameStateUpdated += Game_OnGameStateUpdated;
            game.OnGameFailedToStart += Game_OnGameFailedToStart;
            game.OnGameEnded += Game_OnGameEnded;
            _client.MessageReceived += _client_MessageReceived;

            game.Initialize();
            if (options.Bet == 0)
                await ReplyConfirmLocalizedAsync(strs.connect4_created);
            else
                await ReplyErrorLocalizedAsync(strs.connect4_created_bet(options.Bet + CurrencySign));

            Task _client_MessageReceived(SocketMessage arg)
            {
                if (ctx.Channel.Id != arg.Channel.Id)
                    return Task.CompletedTask;

                var _ = Task.Run(async () =>
                {
                    var success = false;
                    if (int.TryParse(arg.Content, out var col)) success = await game.Input(arg.Author.Id, col);

                    if (success)
                    {
                        try { await arg.DeleteAsync(); }
                        catch { }
                    }
                    else
                    {
                        if (game.CurrentPhase is Connect4Game.Phase.Joining or Connect4Game.Phase.Ended) return;
                        RepostCounter++;
                        if (RepostCounter == 0)
                            try { msg = await ctx.Channel.SendMessageAsync("", embed: (Embed)msg.Embeds.First()); }
                            catch { }
                    }
                });
                return Task.CompletedTask;
            }

            Task Game_OnGameFailedToStart(Connect4Game arg)
            {
                if (_service.Connect4Games.TryRemove(ctx.Channel.Id, out var toDispose))
                {
                    _client.MessageReceived -= _client_MessageReceived;
                    toDispose.Dispose();
                }

                return ErrorLocalizedAsync(strs.connect4_failed_to_start);
            }

            Task Game_OnGameEnded(Connect4Game arg, Connect4Game.Result result)
            {
                if (_service.Connect4Games.TryRemove(ctx.Channel.Id, out var toDispose))
                {
                    _client.MessageReceived -= _client_MessageReceived;
                    toDispose.Dispose();
                }

                string title;
                if (result == Connect4Game.Result.CurrentPlayerWon)
                    title = GetText(strs.connect4_won(Format.Bold(arg.CurrentPlayer.Username),
                        Format.Bold(arg.OtherPlayer.Username)));
                else if (result == Connect4Game.Result.OtherPlayerWon)
                    title = GetText(strs.connect4_won(Format.Bold(arg.OtherPlayer.Username),
                        Format.Bold(arg.CurrentPlayer.Username)));
                else
                    title = GetText(strs.connect4_draw);

                return msg.ModifyAsync(x => x.Embed = _eb.Create()
                                                         .WithTitle(title)
                                                         .WithDescription(GetGameStateText(game))
                                                         .WithOkColor()
                                                         .Build());
            }
        }

        private async Task Game_OnGameStateUpdated(Connect4Game game)
        {
            var embed = _eb.Create()
                           .WithTitle($"{game.CurrentPlayer.Username} vs {game.OtherPlayer.Username}")
                           .WithDescription(GetGameStateText(game))
                           .WithOkColor();


            if (msg is null)
                msg = await ctx.Channel.EmbedAsync(embed);
            else
                await msg.ModifyAsync(x => x.Embed = embed.Build());
        }

        private string GetGameStateText(Connect4Game game)
        {
            var sb = new StringBuilder();

            if (game.CurrentPhase is Connect4Game.Phase.P1Move or Connect4Game.Phase.P2Move)
                sb.AppendLine(GetText(strs.connect4_player_to_move(Format.Bold(game.CurrentPlayer.Username))));

            for (var i = Connect4Game.NumberOfRows; i > 0; i--)
            {
                for (var j = 0; j < Connect4Game.NumberOfColumns; j++)
                {
                    var cur = game.GameState[i + (j * Connect4Game.NumberOfRows) - 1];

                    if (cur == Connect4Game.Field.Empty)
                        sb.Append("⚫"); //black circle
                    else if (cur == Connect4Game.Field.P1)
                        sb.Append("🔴"); //red circle
                    else
                        sb.Append("🔵"); //blue circle
                }

                sb.AppendLine();
            }

            for (var i = 0; i < Connect4Game.NumberOfColumns; i++) sb.Append(numbers[i]);
            return sb.ToString();
        }
    }
}