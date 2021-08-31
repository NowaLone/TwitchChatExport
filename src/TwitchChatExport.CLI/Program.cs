using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TwitchGQL.Client;
using TwitchGQL.Models.Enums;
using TwitchGQL.Models.Requests.Persisted;

namespace ChatLogViewer.Console
{
    internal class Program
    {
        private static int Main(string[] args)
        {
            System.Console.OutputEncoding = new System.Text.UTF8Encoding(true);
            Directory.CreateDirectory("Logs");

            var rootCommand = new RootCommand()
            {
                new Option<string>(new[] { "--client-id" }, () => "kimne78kx3ncx6brgo4mv6wki5h1ko", "Twitch client id.") { IsRequired = true },
                new Option<string>(new[] { "-t", "--token" }, "Twitch OAuth token. How to get? Log in at twitch.tv an open deveploment tools (google it), go to console (google it) and write 'cookies['auth-token']'.") { IsRequired = true },
                new Option<string>(new[] { "-c", "--channel" }, "Twitch channel.") { IsRequired = true },
                new Option<string>(new[] { "-u", "--user" }, "Twitch user id or username.") { IsRequired = true },
                new Option<bool>(new[] { "-a", "--auto" }, "Export logs until the end.") { IsRequired = false },
                new Option<bool>(new[] { "-oo", "--original-output" }, "Write original json output.") { IsRequired = false },
            };

            rootCommand.Description = "Twitch chatlog exporter";
            rootCommand.Handler = CommandHandler.Create<string, string, string, string, bool, bool, CancellationToken>(Execute);

            return rootCommand.InvokeAsync(args).Result;
        }

        public static async Task Execute(string clientId, string token, string channel, string user, bool auto, bool originalOutput, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(clientId))
            {
                throw new ArgumentException($"'{nameof(clientId)}' cannot be null or empty.", nameof(clientId));
            }

            if (string.IsNullOrEmpty(token))
            {
                throw new ArgumentException($"'{nameof(token)}' cannot be null or empty.", nameof(token));
            }

            if (token.StartsWith("OAuth "))
            {
                token = token.Substring(6, token.Length - 6);
            }

            if (string.IsNullOrEmpty(channel))
            {
                throw new ArgumentException($"'{nameof(channel)}' cannot be null or empty.", nameof(channel));
            }

            if (string.IsNullOrEmpty(user))
            {
                throw new ArgumentException($"'{nameof(user)}' cannot be null or empty.", nameof(user));
            }

            using (TwitchGQLClient graphQLClient = new TwitchGQLClient() { ClientId = clientId, Authorization = token })
            {
                if (!int.TryParse(user, out int userId))
                {
                    user = (await graphQLClient.SendQueryAsync(new GetUserIDRequest(user, UserLookupType.ALL), cancellationToken).ConfigureAwait(false)).User.Id;
                }

                await MakeRequestAsync(channel, user, auto, originalOutput, graphQLClient, null, cancellationToken).ConfigureAwait(false);
            }
        }

        public static async Task MakeRequestAsync(string channel, string user, bool auto, bool originalOutput, TwitchGQLClient graphQLClient, string cursor, CancellationToken cancellationToken)
        {
            var request = new ViewerCardModLogsMessagesBySenderRequest(user, channel, true, cursor);

            var result = await graphQLClient.SendQueryAsync(request, cancellationToken).ConfigureAwait(false);

            if (originalOutput)
            {
                using (FileStream stream = File.Create($"Logs\\{channel}_{user}_Original_{DateTimeOffset.Now.ToUnixTimeMilliseconds()}.json"))
                {
                    using (var streamWriter = new StreamWriter(stream, new System.Text.UTF8Encoding(true)))
                    {
                        await streamWriter.WriteAsync(System.Text.Json.JsonSerializer.Serialize(result)).ConfigureAwait(false);
                    }
                }
            }

            if (result.Channel.ModLogs.MessagesBySender != null && result.Channel.ModLogs.MessagesBySender.Edges.Any())
            {
                using (FileStream stream = File.Open($"Logs\\{channel}_{user}.log", FileMode.Append, FileAccess.Write))
                {
                    using (var streamWriter = new StreamWriter(stream, new System.Text.UTF8Encoding(true)))
                    {
                        foreach (var edge in result.Channel.ModLogs.MessagesBySender.Edges)
                        {
                            if (edge.Node.Sender != null)
                            {
                                System.Console.WriteLine($"{edge.Node.SentAt} {edge.Node.Sender.DisplayName}: {edge.Node.Content.Text}");
                                await streamWriter.WriteLineAsync($"{edge.Node.SentAt} {edge.Node.Sender.DisplayName}: {edge.Node.Content.Text}").ConfigureAwait(false);
                            }
                        }
                    }
                }

                if (result.Channel.ModLogs.MessagesBySender.PageInfo.HasNextPage)
                {
                    if (!auto)
                    {
                        ConsoleKeyInfo key;
                        do
                        {
                            System.Console.WriteLine("Press Enter to continue or Esc to exit.");
                            key = System.Console.ReadKey(true);
                            if (key.Key == ConsoleKey.Escape)
                            {
                                return;
                            }
                        }
                        while (key.Key != ConsoleKey.Enter);
                    }

                    cursor = result.Channel.ModLogs.MessagesBySender.Edges.Last().Cursor;
                    await MakeRequestAsync(channel, user, auto, originalOutput, graphQLClient, cursor, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }
}