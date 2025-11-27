using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecretCode
{
    public sealed class GuessFeedback
    {
        public string Guess { get; init; }
        public int Exact { get; init; }
        public int Partial { get; init; }
        public int AttemptNumber { get; init; }

        [JsonConstructor]
        public GuessFeedback(string guess, int exact, int partial, int attemptNumber)
        {
            Guess = guess;
            Exact = exact;
            Partial = partial;
            AttemptNumber = attemptNumber;
        }
    }

    public sealed class Game
    {
        public int CodeLength { get; init; } = 4;
        public string AllowedColors { get; init; } = "rygbmc";
        public int MaxAttempts { get; init; } = 9;

        public List<GuessFeedback> History { get; private set; } = new();
        public bool IsWon { get; private set; } = false;
        public bool IsOver => IsWon || History.Count >= MaxAttempts || _surrendered;

        [JsonInclude]
        public string _secret { get; private set; } = string.Empty;

        internal bool _surrendered = false;

        public Game() { }

        public static Game NewRandom(int codeLength = 4, string allowedColors = "rygbmc", int maxAttempts = 9, int? seed = null)
        {
            var rnd = seed.HasValue ? new Random(seed.Value) : new Random();
            var g = new Game
            {
                CodeLength = codeLength,
                AllowedColors = allowedColors,
                MaxAttempts = maxAttempts
            };

            var arr = new char[codeLength];
            for (int i = 0; i < codeLength; i++)
            {
                arr[i] = allowedColors[rnd.Next(allowedColors.Length)];
            }
            g._secret = new string(arr);
            return g;
        }

        // Make a guess; throws ArgumentException on invalid guess.
        // Returns feedback for the guess and updates history and state.
        public GuessFeedback MakeGuess(string guess)
        {
            if (IsOver)
                throw new InvalidOperationException("Game is already over.");

            if (!TryNormalizeGuess(guess, out var norm))
                throw new ArgumentException($"Guess must be {CodeLength} letters from [{AllowedColors}].", nameof(guess));

            var (exact, partial) = Evaluate(norm.ToCharArray(), _secret.ToCharArray());
            var feedback = new GuessFeedback(norm, exact, partial, History.Count + 1);
            History.Add(feedback);
            if (exact == CodeLength)
                IsWon = true;
            return feedback;
        }

        public (int exact, int partial) Evaluate(char[] guess, char[] secret)
        {
            if (guess == null || secret == null || guess.Length != secret.Length)
                throw new ArgumentException("Guess and secret must be non-null and same length.");

            int length = guess.Length;
            int exact = 0;
            var secretUsed = new bool[length];
            var guessUsed = new bool[length];

            for (int i = 0; i < length; i++)
            {
                if (guess[i] == secret[i])
                {
                    exact++;
                    secretUsed[i] = true;
                    guessUsed[i] = true;
                }
            }

            var freq = new Dictionary<char, int>();
            for (int i = 0; i < length; i++)
            {
                if (secretUsed[i]) continue;
                if (!freq.ContainsKey(secret[i])) freq[secret[i]] = 0;
                freq[secret[i]]++;
            }

            int partial = 0;
            for (int i = 0; i < length; i++)
            {
                if (guessUsed[i]) continue;
                var g = guess[i];
                if (freq.TryGetValue(g, out var count) && count > 0)
                {
                    partial++;
                    freq[g] = count - 1;
                }
            }

            return (exact, partial);
        }

        // 'B' = Black (exact), 'W' = White (partial), 'N' = No match.
        public char[] EvaluatePositions(string guess)
        {
            if (!TryNormalizeGuess(guess, out var norm))
                throw new ArgumentException($"Guess must be {CodeLength} letters from [{AllowedColors}].", nameof(guess));

            var secret = _secret.ToCharArray();
            var g = norm.ToCharArray();
            int length = CodeLength;
            var result = Enumerable.Repeat('N', length).ToArray();
            var secretUsed = new bool[length];
            var guessUsed = new bool[length];

            for (int i = 0; i < length; i++)
            {
                if (g[i] == secret[i])
                {
                    result[i] = 'B';
                    secretUsed[i] = true;
                    guessUsed[i] = true;
                }
            }

            var freq = new Dictionary<char, int>();
            for (int i = 0; i < length; i++)
            {
                if (secretUsed[i]) continue;
                if (!freq.TryGetValue(secret[i], out var v)) v = 0;
                freq[secret[i]] = v + 1;
            }

            for (int i = 0; i < length; i++)
            {
                if (guessUsed[i]) continue;
                var ch = g[i];
                if (freq.TryGetValue(ch, out var count) && count > 0)
                {
                    result[i] = 'W';
                    freq[ch] = count - 1;
                }
            }

            return result;
        }

        public string RevealSecret(bool forceReveal = false)
        {
            if (!IsOver && !forceReveal)
                throw new InvalidOperationException("Secret cannot be revealed until the game is over.");

            return _secret;
        }

        public void Surrender()
        {
            _surrendered = true;
        }

        public void Save(string path)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var snapshot = new GameSnapshot(this);
            var txt = JsonSerializer.Serialize(snapshot, options);
            File.WriteAllText(path, txt);
        }

        public static Game Load(string path)
        {
            var txt = File.ReadAllText(path);
            var snapshot = JsonSerializer.Deserialize<GameSnapshot>(txt)
                ?? throw new InvalidOperationException("Failed to deserialize game snapshot.");
            return snapshot.ToGame();
        }

        public bool TryNormalizeGuess(string raw, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            var s = raw.Trim().ToLowerInvariant().Replace(" ", string.Empty);
            if (s.Length != CodeLength) return false;
            if (s.Any(c => !AllowedColors.Contains(c))) return false;
            normalized = s;
            return true;
        }

        private sealed class GameSnapshot
        {
            public int CodeLength { get; set; }
            public string AllowedColors { get; set; } = "";
            public int MaxAttempts { get; set; }
            public List<GuessFeedback> History { get; set; } = new();
            public bool IsWon { get; set; }
            public string Secret { get; set; } = "";
            public bool Surrendered { get; set; }

            public GameSnapshot() { }

            public GameSnapshot(Game g)
            {
                CodeLength = g.CodeLength;
                AllowedColors = g.AllowedColors;
                MaxAttempts = g.MaxAttempts;
                History = new List<GuessFeedback>(g.History);
                IsWon = g.IsWon;
                Secret = g._secret;
                Surrendered = g._surrendered;
            }

            public Game ToGame()
            {
                var g = new Game
                {
                    CodeLength = CodeLength,
                    AllowedColors = AllowedColors,
                    MaxAttempts = MaxAttempts,
                    _secret = Secret,
                    History = new List<GuessFeedback>(History),
                    IsWon = IsWon,
                    _surrendered = Surrendered
                };
                return g;
            }
        }
    }

    internal static class Program
    {
        private static readonly Dictionary<char, ConsoleColor> ColorMap = new()
        {
            ['r'] = ConsoleColor.Red,
            ['y'] = ConsoleColor.Yellow,
            ['g'] = ConsoleColor.Green,
            ['b'] = ConsoleColor.Blue,
            ['m'] = ConsoleColor.Magenta,
            ['c'] = ConsoleColor.Cyan
        };

        private static void Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("Mastermind — Computer vs Player (classic)");
            Console.WriteLine("Colors: r=Red, y=Yellow, g=Green, b=Blue, m=Magenta, c=Cyan");
            Console.WriteLine("Commands: 's' to surrender, 'save' to save, 'load' to load snapshot, 'q' to quit.");
            Console.WriteLine();

            Game game = null!;
            while (true)
            {
                Console.Write("Start a new game? (y/n) ");
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Y)
                {
                    game = Game.NewRandom(codeLength: 4, allowedColors: "rygbmc", maxAttempts: 9);
                    break;
                }
                if (key.Key == ConsoleKey.N)
                {
                    Console.Write("Enter path to snapshot to load (or blank to start new): ");
                    var path = Console.ReadLine() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        game = Game.NewRandom(codeLength: 4, allowedColors: "rygbmc", maxAttempts: 9);
                        break;
                    }
                    try
                    {
                        game = Game.Load(path);
                        Console.WriteLine($"Loaded snapshot from '{path}'.");
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Load failed: {ex.Message}");
                    }
                }
            }
            RunCli(game);
        }

        private static void RunCli(Game game)
        {
            while (!game.IsOver)
            {
                Console.WriteLine();
                Console.WriteLine($"Attempt {game.History.Count + 1} of {game.MaxAttempts}. Enter guess (e.g. rygb) or command:");
                Console.Write("> ");
                var input = (Console.ReadLine() ?? string.Empty).Trim();

                if (string.Equals(input, "q", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Quit requested. Exiting without revealing secret.");
                    return;
                }

                if (string.Equals(input, "s", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(input, "surrender", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("You surrendered. Secret revealed:");
                    game.Surrender();
                    var secret = game.RevealSecret(forceReveal: true);
                    PrintColoredCode(secret);
                    break;
                }

                if (string.Equals(input, "save", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Write("Enter filename to save snapshot: ");
                    var path = Console.ReadLine() ?? string.Empty;
                    try
                    {
                        game.Save(path);
                        Console.WriteLine($"Game saved to '{path}'.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Save failed: {ex.Message}");
                    }
                    continue;
                }

                if (string.Equals(input, "load", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Write("Enter filename to load snapshot: ");
                    var path = Console.ReadLine() ?? string.Empty;
                    try
                    {
                        game = Game.Load(path);
                        Console.WriteLine($"Loaded snapshot from '{path}'.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Load failed: {ex.Message}");
                    }
                    continue;
                }

                if (!game.TryNormalizeGuess(input, out var norm))
                {
                    Console.WriteLine($"Invalid guess. Provide exactly {game.CodeLength} letters from [{game.AllowedColors}].");
                    continue;
                }

                try
                {
                    var fb = game.MakeGuess(norm);
                    PrintColoredGuessWithFeedback(game, fb);
                    if (fb.Exact == game.CodeLength)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"Congratulations — you guessed the secret in {fb.AttemptNumber} attempt(s)!");
                        Console.ResetColor();
                        break;
                    }
                    else
                    {
                        int attemptsLeft = game.MaxAttempts - game.History.Count;
                        Console.WriteLine($"Black (exact): {fb.Exact}, White (partial): {fb.Partial} — Attempts left: {attemptsLeft}");
                        if (attemptsLeft == 0)
                        {
                            Console.WriteLine("No attempts left. The secret will be revealed:");
                            var secret = game.RevealSecret(forceReveal: true);
                            PrintColoredCode(secret);
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }

            Console.WriteLine();
            Console.WriteLine("Game summary:");
            foreach (var fb in game.History)
            {
                PrintColoredGuessWithFeedback(game, fb);
            }

            if (game.IsWon)
            {
                Console.WriteLine("Result: WIN");
            }
            else if (game._surrendered)
            {
                Console.WriteLine("Result: SURRENDERED");
            }
            else
            {
                Console.WriteLine("Result: LOST");
            }

            Console.WriteLine("Press any key to exit.");
            Console.ReadKey(true);
        }

        private static void PrintColoredGuessWithFeedback(Game game, GuessFeedback fb)
        {
            PrintColoredCode(fb.Guess);
            var markers = game.EvaluatePositions(fb.Guess); // 'B' black (exact), 'W' white (partial), 'N' none
            var prevFg = Console.ForegroundColor;
            var prevBg = Console.BackgroundColor;

            for (int i = 0; i < markers.Length; i++)
            {
                var m = markers[i];
                switch (m)
                {
                    case 'B':
                        Console.BackgroundColor = ConsoleColor.White;
                        Console.ForegroundColor = ConsoleColor.Black;
                        Console.Write('●');
                        break;
                    case 'W':
                        Console.BackgroundColor = ConsoleColor.DarkGray;
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.Write('○');
                        break;
                    default:
                        Console.Write(' ');
                        break;
                }

                if (i != markers.Length - 1)
                {
                    Console.ForegroundColor = prevFg;
                    Console.BackgroundColor = prevBg;
                    Console.Write(' ');
                }
            }

            Console.ForegroundColor = prevFg;
            Console.BackgroundColor = prevBg;
            Console.WriteLine();

            Console.WriteLine($"  -> Black: {fb.Exact}, White: {fb.Partial} (Attempt #{fb.AttemptNumber})");

            Console.Write("  Legend: ");
            var pfg = Console.ForegroundColor;
            var pbg = Console.BackgroundColor;

            Console.BackgroundColor = ConsoleColor.White;
            Console.ForegroundColor = ConsoleColor.Black;
            Console.Write("●");
            Console.ForegroundColor = pfg;
            Console.BackgroundColor = pbg;
            Console.Write(" = Black ");

            Console.BackgroundColor = ConsoleColor.DarkGray;
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("○");
            Console.ForegroundColor = pfg;
            Console.BackgroundColor = pbg;
            Console.WriteLine(" = White ");
        }

        private static void PrintColoredCode(string code)
        {
            foreach (var ch in code)
            {
                if (ColorMap.TryGetValue(ch, out var color))
                {
                    var prev = Console.ForegroundColor;
                    Console.ForegroundColor = color;
                    Console.Write(ch);
                    Console.ForegroundColor = prev;
                }
                else
                {
                    Console.Write(ch);
                }
                Console.Write(' ');
            }
            Console.WriteLine();
        }
    }
}