using AssetRipper.Export.Configuration;
using AssetRipper.Export.PrimaryContent;
using AssetRipper.Export.UnityProjects;
using AssetRipper.Import.Logging;
using AssetRipper.IO.Files;
using AssetRipper.Processing;

namespace AssetRipper.CLI;

/// <summary>
/// Headless command-line frontend: load game files, export, exit.
/// Mirrors the load/export paths of AssetRipper.GUI.Web.GameFileLoader without the web server,
/// so it can be driven from scripts and other tools (CI, pipelines, rip bridges).
/// </summary>
public static class Program
{
	public static int Main(string[] args)
	{
		List<string> inputs = [];
		string? output = null;
		bool primaryContent = false;
		bool unityProject = false;
		bool verbose = false;

		static string Value(string[] a, ref int i, string name)
		{
			if (++i >= a.Length)
			{
				throw new ArgumentException($"Missing value for {name}");
			}
			return a[i];
		}

		try
		{
			for (int i = 0; i < args.Length; i++)
			{
				string arg = args[i];
				string flag = arg.Contains('=') && arg.StartsWith("--") ? arg[..arg.IndexOf('=')] : arg;
				string inline = flag.Length < arg.Length ? arg[(flag.Length + 1)..] : string.Empty;
				switch (flag)
				{
					case "-o" or "--out" or "--output":
						output = inline.Length > 0 ? inline : Value(args, ref i, flag);
						break;
					case "-p" or "--primary-content":
						primaryContent = true;
						break;
					case "-u" or "--unity-project":
						unityProject = true;
						break;
					case "-v" or "--verbose":
						verbose = true;
						break;
					case "-h" or "--help":
						PrintUsage();
						return 0;
					case "--version":
						Console.WriteLine(typeof(Program).Assembly.GetName().Version);
						return 0;
					default:
						if (flag.StartsWith('-'))
						{
							Console.Error.WriteLine($"Unknown option: {arg}");
							PrintUsage();
							return 2;
						}
						inputs.Add(arg);
						break;
				}
			}
		}
		catch (ArgumentException e)
		{
			Console.Error.WriteLine(e.Message);
			return 2;
		}

		if (inputs.Count == 0)
		{
			Console.Error.WriteLine("No input paths given.");
			PrintUsage();
			return 2;
		}
		if (output is null)
		{
			Console.Error.WriteLine("No output path given (-o/--out).");
			return 2;
		}
		if (!primaryContent && !unityProject)
		{
			primaryContent = true; // default to the cheapest export
		}

		Logger.AllowVerbose = verbose;
		Logger.Add(new ConsoleLogger());

		foreach (string input in inputs)
		{
			if (!Path.Exists(input))
			{
				Console.Error.WriteLine($"Input path does not exist: {input}");
				return 2;
			}
		}

		try
		{
			FullConfiguration settings = new();
			ExportHandler handler = new(settings);
			GameData gameData = handler.LoadAndProcess(inputs, LocalFileSystem.Instance);

			if (primaryContent)
			{
				string? path = PrepareOutputDirectory(output, "primary content");
				if (path is null)
				{
					return 3;
				}
				Logger.Info(LogCategory.Export, $"Exporting primary content to {path}");
				settings.ExportRootPath = path;
				PrimaryContentExporter.CreateDefault(gameData, settings)
					.Export(gameData.GameBundle, settings, LocalFileSystem.Instance);
				Logger.Info(LogCategory.Export, "Finished exporting primary content.");
			}

			if (unityProject)
			{
				string? path = PrepareOutputDirectory(
					primaryContent ? Path.Combine(output, "ExportedProject") : output,
					"Unity project");
				if (path is null)
				{
					return 3;
				}
				Logger.Info(LogCategory.Export, $"Exporting Unity project to {path}");
				handler.Export(gameData, path, LocalFileSystem.Instance);
				Logger.Info(LogCategory.Export, "Finished exporting Unity project.");
			}

			return 0;
		}
		catch (Exception e)
		{
			Logger.Error(LogCategory.None, "Fatal error:", e);
			return 1;
		}
	}

	/// <returns>The directory to export into, or null if the target is non-empty and must be cleared manually.</returns>
	private static string? PrepareOutputDirectory(string path, string what)
	{
		if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
		{
			Console.Error.WriteLine($"Output directory for {what} is not empty: {path}");
			Console.Error.WriteLine("Delete it first, or choose another path.");
			return null;
		}
		Directory.CreateDirectory(path);
		return path;
	}

	private static void PrintUsage()
	{
		Console.WriteLine("""
			AssetRipper.CLI — headless load & export

			Usage:
			  AssetRipper.CLI <input...> -o <output-dir> [options]

			Inputs:
			  One or more game files or folders (APK, split APKs, unpacked folders,
			  bundles, serialized files). Multiple paths share one load session.

			Options:
			  -o, --out <dir>       Output directory (required)
			  -p, --primary-content Export primary content: assemblies (dummy DLLs),
			                        textures, and other engine assets as JSON/PNG.
			                        This is the default when no export mode is given.
			  -u, --unity-project   Export a Unity project (ExportedProject layout).
			                        Can be combined with -p (project lands in
			                        <out>/ExportedProject).
			  -v, --verbose         Verbose logging
			  -h, --help            Show this help
			""");
	}
}
