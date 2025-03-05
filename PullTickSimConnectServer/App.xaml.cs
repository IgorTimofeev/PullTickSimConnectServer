using Newtonsoft.Json;
using System.IO;
using System.Windows;

namespace PullTickSimConnectServer;

public partial class App : Application {
	public App() {
		LoadSettings();
	}

	public static readonly string AppDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Charlotte Aviation", "Simlink");
	public static readonly string SettingsPath = Path.Combine(AppDataPath, "Settings.json");

	public static SettingsJSON Settings { get; private set; } = new();

	protected override void OnExit(ExitEventArgs e) {
		SaveSettings();

		base.OnExit(e);
	}

	static void LoadSettings() {
		if (File.Exists(SettingsPath)) {
			try {
				Settings = JsonConvert.DeserializeObject<SettingsJSON>(File.ReadAllText(SettingsPath)) ?? new();
			}
			catch (Exception ex) {
				Settings = new();
				MessageBox.Show(ex.Message);
			}
		}
	}

	static void SaveSettings() {
		Directory.CreateDirectory(AppDataPath);

		File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(Settings!, Formatting.Indented));
	}
}