using System;
using Mono.Terminal;
using Mono.Options;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Couchbase.Lite;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using System.Globalization;

namespace cblogviewer
{
    class MainClass
    {
        static bool _showHelp;
        static string _helpText;

        static bool _skip;

        static string _path;

        static string _cb_path;

        static Manager _manager;

        static Database _database;

        static Dictionary<string, FileInfo> _logs;

        static readonly ConsoleColor[] _colors = new[] { ConsoleColor.Magenta, ConsoleColor.Cyan, ConsoleColor.Green, ConsoleColor.Blue, ConsoleColor.Red, ConsoleColor.Yellow, ConsoleColor.DarkCyan, ConsoleColor.DarkGreen, ConsoleColor.DarkMagenta, ConsoleColor.DarkRed, ConsoleColor.DarkYellow };

        static readonly ConsoleColor _defaultColor = Console.ForegroundColor;

        static int _cursorLeft;

        static int _cursorTop;

        public static void Main(string[] args)
        {
            ParseArgs(args);

            if (Environment.ExitCode > 0)
            {
                return;
            }

            if (!LoadLogcatPaths())
            {
                Environment.ExitCode = 100;
                OutputLineUsingColor(ConsoleColor.Red, "Error loading logcat files from the directory at `{0}`", _path);
                return;
            }


            if (!OpenDatabase())
            {
                if (_manager != null) _manager.Close();
                return;
            }

            if (!_skip) ImportLogcatFiles();

            #if USE_CURSES
            RunCursesUI();
            #else
            RunLocalQuery();
            #endif
        }

        static bool OpenDatabase()
        {
            var isOpen = false;

            if (String.IsNullOrWhiteSpace(_cb_path))
            {
                Environment.ExitCode = 101;
                OutputLineUsingColor(ConsoleColor.Red, "The provided database path was empty or contained only whitespace characters.");
                return isOpen;
            }

            var cbPathDir = Path.GetDirectoryName(_cb_path);

            if (!Directory.Exists(cbPathDir))
            {
                Environment.ExitCode = 101;
                OutputLineUsingColor(ConsoleColor.Red, "The parent directory must already exist for the path `{0}`", _cb_path);
                return isOpen;
            }

            if (!Path.GetExtension(_cb_path).Equals(".cblite2"))
            {
                Environment.ExitCode = 101;
                OutputLineUsingColor(ConsoleColor.Red, "The file extension must be `.cblite2`.");
                return isOpen;
            }

            try
            {
                var options = new ManagerOptions();

                _manager = new Manager(new DirectoryInfo(cbPathDir), options);
                var cbPathInfo = new DirectoryInfo(_cb_path);
                if (!_skip && cbPathInfo.Exists)
                {
                    if (Prompt(ConsoleColor.Yellow, "The database file `{0}` already exists. Overwrite? [Y/n]", _cb_path) == (int)ConsoleKey.Y)
                    {
                        try 
                        {
                            Directory.Delete(_cb_path, true);
                        }
                        catch (Exception)
                        {
                            OutputLineUsingColor(ConsoleColor.Red, "Unable to delete existing database at `{0}`.", _cb_path);
                            Environment.ExitCode = 103;
                            return false;
                        }
                    }
                }
                #if USE_SQLITE
                _database = _manager.GetDatabase(Path.GetFileNameWithoutExtension(_cb_path));
                #else
                _database = _manager.OpenDatabase(Path.GetFileNameWithoutExtension(_cb_path), new DatabaseOptions { Create = true, StorageType = DatabaseOptions.FORESTDB_STORAGE });
                #endif
                isOpen = true;
            }
            catch (Exception e)
            {
                
                Environment.ExitCode = 102;
                OutputLineUsingColor(ConsoleColor.Red, "Unable to open/create a database at path `{0}`", _cb_path);
                OutputLineUsingColor(ConsoleColor.DarkRed, e.ToString());
            }

            return isOpen;
        }

        static void ImportLogcatFiles()
        {
            foreach(var log in _logs)
            {
                OutputLineUsingColor(ConsoleColor.Blue, "Processing logs for {0}", log.Key);
                _cursorLeft = Console.CursorLeft;
                _cursorTop = Console.CursorTop;

                _database.RunInTransaction(()=>{ Import(log.Key, log.Value); return true; });

                Console.WriteLine("Done!");
            }
        }

        static void UpdateProgress(float value)
        {
            Console.SetCursorPosition(_cursorLeft, _cursorTop);
            Console.Write(value.ToString("P0"));
            Console.Write(" complete... ");
        }

        static void Import(string device, FileInfo log)
        {
            var line = String.Empty;
            IDictionary<string, object> props;

            using(var fs = log.OpenText())
            {
                var section = String.Empty;

                var length = log.Length;

                while (!fs.EndOfStream)
                {
                    line = fs.ReadLine();

                    // Skip the headers, but determine which log we're in.
                    if (line.StartsWith("---------", StringComparison.Ordinal))
                    {
                        section = line.Substring(23);
                        continue;
                    }

                    props = new Dictionary<string, object>(6)
                    {
                        { "section", section },
                        { "device", device }
                    };

                    ReadTimestamp(line, props);
                    ReadVerbosity(line, props);
                    ReadTag(line, props);
                    ReadPID(line, props);
                    ReadMessage(line, props);

                    Insert(props);

                    UpdateProgress(fs.BaseStream.Position / (float)length);
                }
            }
        }

        static void ReadTimestamp(string line, IDictionary<string, object> props)
        {
            // Exemplar: 
            //      03-19 12:54:12.527 D/Listener(28526): authHeader is null

            var monthDay = line.Substring(0, 5);
            var time = line.Substring(6, 12);
            var year = DateTime.UtcNow.Year; // FIXME: Need to add logic to detect and wrap around 12/31.
            var timeStamp = DateTime.Parse(String.Format("{0}-{1}T{2}", year, monthDay, time)); // FIXME    : assumes current DST/timezone for logs.

            props["timeStamp"] = TimeZone.CurrentTimeZone.ToUniversalTime(timeStamp);
        }

        static void ReadVerbosity(string line, IDictionary<string, object> props)
        {
            // Exemplar: 
            //      03-19 12:54:12.527 D/Listener(28526): authHeader is null

            var verbosity = line.Substring(19, 1);

            props["verbosity"] = verbosity;
        }

        static void ReadTag(string line, IDictionary<string, object> props)
        {
            // Exemplar: 
            //      03-19 12:54:12.527 D/Listener(28526): authHeader is null

            var leftParen = line.IndexOf('(', 21);
            var tag = line.Substring(21, leftParen - 21);

            props["tag"] = tag.Trim();
        }

        static void ReadPID(string line, IDictionary<string, object> props)
        {
            // Exemplar: 
            //      03-19 12:54:12.527 D/Listener(28526): authHeader is null

            var leftParen = line.IndexOf('(', 21) + 1;
            var rightParen = line.IndexOf(')', leftParen);
            var pid = line.Substring(leftParen, rightParen - leftParen);

            props["pid"] = pid.Trim();
        }

        static void ReadMessage(string line, IDictionary<string, object> props)
        {
            // Exemplar: 
            //      03-19 12:54:12.527 D/Listener(28526): authHeader is null

            var colon = line.IndexOf(':', 21);
            var message = line.Substring(colon + 2);

            props["message"] = message;
        }

        static void Insert(IDictionary<string, object> props)
        {
            var doc = _database.CreateDocument();
            var result = doc.PutProperties(props);

            if (result == null)
            {
                OutputLineUsingColor(ConsoleColor.Red, "Error inserting a new document into the database.");
            }
        }

        static bool LoadLogcatPaths()
        {
            var isValid = false;
            isValid = Directory.Exists(_path);

            if (isValid)
            {
                var dir = new DirectoryInfo(_path);
                var logcatInfo = new Dictionary<string, FileInfo>();
                var dirName = String.Empty;
                var file = default(FileInfo);

                foreach(var fsEntry in dir.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
                {
                    // Store directory names as device key.
                    if (fsEntry.Exists && fsEntry is DirectoryInfo)
                    {
                        logcatInfo[fsEntry.Name] = null;
                    }
                    else
                    {
                        if (fsEntry.Exists && fsEntry.Name.Equals("logcat.txt"))
                        {
                            file = (FileInfo)fsEntry;
                            dirName = file.Directory.Name;
                            logcatInfo[dirName] = file;
                        }
                    }
                }

                _logs = logcatInfo;
                isValid = true;
            }

            return isValid;
        }

        #region CLI

        static void ParseArgs(string[] args)
        {
            var options = new OptionSet() {
                { "s|skip-import", "skip importing the logs", v => _skip = (v == "s") || (v == "skip-import") },
                { "f|file=",  "database file path", v => _cb_path = v },
                { "h|help",  "show this message and exit", v => _showHelp = v != null },
            };

            List<string> extra;
            try {
                extra = options.Parse(args);
                if (extra.Count == 1)
                {
                    // Should be just a file path. Will validate below.
                    _path = extra[0];
                    if (!String.IsNullOrWhiteSpace(_cb_path))
                    {
                        Trace.WriteLine("Database: {0}", _cb_path);
                    }
                    Trace.WriteLine("Dump dir: {0}", _path);
                }
            } catch (OptionException e) {
                Console.Write("cb-logviewer: ");
                OutputLineUsingColor(
                    color: ConsoleColor.Red, 
                    format: e.Message
                );
                Console.WriteLine();
                Console.WriteLine("Try `cblogviewer --help' for more information.");
                Environment.ExitCode = 99;
                return;
            }

            if (_showHelp || args.Length == 0) {
                var writer = new StringWriter(new StringBuilder("usage: cb-logviewer [options] [directory path]" + Environment.NewLine + Environment.NewLine));
                options.WriteOptionDescriptions(writer);

                _helpText = writer.ToString();

                writer.Close();

                ShowHelp();
                return;
            }

        }

        static void ShowHelp()
        {
            Console.WriteLine(_helpText);
        }

        static int Prompt(ConsoleColor color, string format, params string[] args)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(format, args);
            Console.ForegroundColor = _defaultColor;
            try 
            {
                Console.CursorVisible = true;
                return Console.Read();                
            }
            finally
            {
                Console.CursorVisible = true;
            }

        }
            
        static void OutputLineUsingColor(ConsoleColor color, string format, params object[] args)
        {
            OutputUsingColor(color, format, args);
            Console.Write(Environment.NewLine);
        }
            
        static void OutputUsingColor(ConsoleColor color, string format, params object[] args)
        {
            Console.ForegroundColor = color;
            Console.Write(format, args);
            Console.ForegroundColor = _defaultColor;
        }

        static void RunLocalQuery()
        {
            var view = _database.GetView("byTime");
            view.SetMap(
                (doc, emit) => 
                    emit(
//                        new[]
//                        {
                    ((DateTime)doc["timeStamp"]).ToString("o")
//                        ,
//                            doc["tag"],
//                            doc["verbosity"]
//                        }, 
                        ,null
                    ),
                "2"
            );                

            var keys = new Dictionary<string, string>();

            Console.Write("Enter your start time: ");
            var startTime = Console.ReadLine();
            if (!String.IsNullOrWhiteSpace(startTime)) keys["timeStamp"] = startTime;

            Console.Write("Enter your stop time: ");
            var endTime = Console.ReadLine();

            var endKeys = new List<object>(keys.Values);
            if (!String.IsNullOrWhiteSpace(endTime))
                endKeys[0] =  endTime;

            var query = view.CreateQuery();
            if (keys.Values.Count > 0)
                query.StartKey = keys.Values.ElementAt(0);

            if (endKeys.Count > 0)
                query.EndKey = endKeys[0];

            OutputUsingColor(ConsoleColor.Green, "Running query...");
            var results = query.Run();
            OutputLineUsingColor(ConsoleColor.Green, "Done!");
            var _colorMap = new Dictionary<string,ConsoleColor>();

            ConsoleColor deviceColor;
            foreach (var row in results)
            {
                if (row.Document == null) continue;
                var device = (string)row.Document.GetProperty("device");

                if (!_colorMap.ContainsKey(device))
                {
                    _colorMap[device] = _colors[_colorMap.Keys.Count % 11];
                }
                deviceColor = (ConsoleColor)_colorMap[device];

                OutputLineUsingColor(deviceColor, "{0} {1} {2} {3}/{4}: {5}", row.Key, device, row.Document.GetProperty("tag"), row.Document.GetProperty("verbosity"), row.Document.GetProperty("pid"), row.Document.GetProperty("message"));
            }
        }

        #endregion

        #region Curses UI

        static Frame _container;

        static void RunCursesUI()
        {
            SetupCurses();

            Application.Init(false);
            _container = BuildWindow();
            Application.Run(_container);

            TeardownCurses();
        }
        
        static Frame BuildWindow()
        {
            var f = new Frame("cb-logviewer");
            f.Border = 0;
            f.Fill = Fill.None;
            f.Container.Border = 0;
            f.Container.Fill = Fill.None;

            var b = new Button("Run Query");
            b.Clicked += RunQueryNow;
            f.Add(b);
//            var l = new Label(0,0, "Results:");
            var c = new Frame("Results:");
            c.Border = 1;
            f.Add(c);
            return f;
        }

        static Dialog BuildUI()
        {
            var d = new Dialog(40, 8, "Hello");
            d.Add(new Label(0, 0, "Hello World"));

            return d;
        }

        static void RunQueryNow (object sender, EventArgs e)
        {
            var d = new Dialog(100, 100, "Run Query");
            d.Add(new Label(0,0, "Query Options:"));
            var start = new Mono.Terminal.Entry(12, 0, 25, null);
            d.Add(start);

            _container.Add(d);
        }

        static void SetupCurses()
        {
            // Standard Init sequence
            Curses.initscr ();
            Curses.cbreak ();
            Curses.noecho ();

            // Recommended
            Curses.nonl ();
        }

        static void TeardownCurses()
        {
            // Shutdown
            Curses.endwin ();
        }

        #endregion
    }
}
