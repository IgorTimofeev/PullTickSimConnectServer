using Pizda;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace PullTickSimConnectServer;

public partial class MainWindow : Window {
	public MainWindow() {
		InitializeComponent();

		// Sim
		Sim = new(this);
		Sim.IsConnectedChanged += UpdateStatus;

		// Controls
		TCPPortTextBox.Text = App.Settings.port.ToString();

		Loaded += (s, e) => {
			Sim.Start();

			// Serial
			Serial.DataReceived += OnSerialDataReceived;

			Serial.Start();

			UpdateStatus();
		};
	}

	public const float EarthEquatorialRadiusM = 6378137f;
	public const float EarchPolarRadiusM = 6356752.3142f;

	public object AicraftDataSyncRoot { get; init; } = new object();
	public object AircraftPacketSyncRoot { get; init; } = new object();

	public object SimmDataSyncRoot { get; init; } = new object();
	public object SimmPacketSyncRoot { get; init; } = new object();

	public AircraftPacket AircraftPacket = new() {
		
	};

	public SimPacket SimPacket = new();

	public SimmData SimmData { get; init; } = new();
	public AicraftData AicraftData { get; init; } = new();

	public static unsafe int RemotePacketSize => sizeof(AircraftPacket);
	public static unsafe int AircraftPacketSize => sizeof(SimPacket);

	readonly Sim Sim;
	Serial Serial = new();

	public void SimDataToSimmData(SimData simData) {
		// Sim data
		lock (SimmDataSyncRoot) {
			SimmData.AccelerationX = FeetToMeters(simData.AccelerationBodyXFt);

			SimmData.RollRad = -simData.RollRad;
			SimmData.PitchRad = -simData.PitchRad;
			SimmData.YawRad = -simData.YawMagneticRad;

			SimmData.SpeedMPS = KnotsToMetersPerSecond(simData.AirSpeedKt);

			SimmData.LatitudeRad = simData.LatitudeRad;
			SimmData.LongitudeRad = simData.LongitudeRad;

			SimmData.PressurePA = simData.PressureHPa * 100;
			SimmData.TemperatureC = simData.TemperatureC;
		}
	}

	public void AicraftDataToSimEvents() {
		lock (AicraftData) {
			// Throttle
			Sim.SendThrottle1Event(AicraftData.Throttle);
			Sim.SendThrottle2Event(AicraftData.Throttle);

			Sim.SendElevatorEvent(AicraftData.Elevator);
			Sim.SendAileronsEvent(1 - AicraftData.Ailerons);

			////Sim.SendRudderEvent(1 - RemoteData.Rudder);

			//Sim.SendFlapsEvent(AicraftData.Flaps);
			//Sim.SendSpoilersEvent(AicraftData.Spoilers);

			////Sim.SendGearSetEvent(RemoteData.LandingGear);

			//Sim.SendAltimeterPressureEvent(AicraftData.AltimeterPressurePa);
		}
	}

	public void AircraftPacketToAircraftData() {
		lock (AicraftDataSyncRoot) {
			lock (AircraftPacketSyncRoot) {
				AicraftData.Throttle = AircraftPacket.Throttle;
				AicraftData.Ailerons = AircraftPacket.Ailerons;
				AicraftData.Elevator = AircraftPacket.Elevator;
			}
		}
	}

	public void SimmDataToSimmPacket() {
		lock (SimmDataSyncRoot) {
			lock (SimmPacketSyncRoot) {
				SimPacket.Header = Packet.Header;

				SimPacket.AccelerationX = (float) SimmData.AccelerationX;

				SimPacket.PitchRad = (float) SimmData.PitchRad;
				SimPacket.YawRad = (float) SimmData.YawRad;
				SimPacket.RollRad = (float) SimmData.RollRad;

				SimPacket.SpeedMPS = (float) SimmData.SpeedMPS;

				SimPacket.LatitudeRad = (float) SimmData.LatitudeRad;
				SimPacket.LongitudeRad = (float) SimmData.LongitudeRad;

				SimPacket.PressurePA = (float) SimmData.PressurePA;
				SimPacket.TemperatureC = (float) SimmData.TemperatureC;

			}
		}
	}

	public void HandleReceivedPacket() {
		AircraftPacketToAircraftData();
	}

	public void PrepareSimmPacketToSend() {
		SimmDataToSimmPacket();
	}

	public static double RadiansToDegrees(double radians) => radians / Math.PI * 180d;
	public static double DegreesToRadians(double degrees) => degrees / 180d * Math.PI;

	public static double FeetToMeters(double feet) => feet * 0.3048f;
	public static double MetersToFeet(double meters) => meters / 0.3048f;

	public static double KnotsToMetersPerSecond(double knots) => knots * 0.5144444444f;
	public static double MetersPerSecondToKnots(double metersPerSecond) => metersPerSecond / 0.5144444444f;

	public static double PressureToAltitude(double referencePressurePa, double pressurePa) {
		const double T0 = 288.15;
		const double L = 0.0065;
		const double RS = 287.058;
		const double g = 9.80665;

		return T0 / L * (1 - Math.Pow(pressurePa / referencePressurePa, RS * L / g));
	}

	static unsafe void StructToBytes<T>(T value, byte[] buffer) where T : unmanaged {
		var pointer = (byte*) &value;

		for (int i = 0; i < sizeof(T); i++)
			buffer[i] = pointer[i];
	}

	static unsafe T BytesToStruct<T>(byte[] buffer) where T : unmanaged {
		T value = default;

		var pointer = (byte*) &value;

		for (int i = 0; i < sizeof(T); i++)
			pointer[i] = buffer[i];

		return value;
	}

	void OnWindowCloseButtonClick(object sender, RoutedEventArgs e) {
		Close();
	}

	void OnWindowMinimizeButtonClick(object sender, RoutedEventArgs e) {
		WindowState = WindowState.Minimized;
	}

	void OnTopPanelMouseDown(object sender, MouseButtonEventArgs e) {
		DragMove();
	}

	void UpdateStatus() {
		var state = Sim.IsConnected;

		StatusEllipse.Stroke = (SolidColorBrush) Application.Current.Resources[
			state
			? "ThemeAccent1"
			: "ThemeAccent3"
		];

		StatusEffect.Opacity = state ? 0.4 : 0;

		StatusImage.Source = new BitmapImage(new Uri($"Resources/Images/{(state ? "LogoOn" : "LogoOff")}.png", UriKind.Relative));

		BackgroundRectangle.StrokeThickness = state ? 1 : 0;

		TCPStatusRun.Text = true ? "running" : "stopped";
		SimStatusRun.Text = Sim.IsConnected ? "running" : "not found";
	}

	unsafe void OnSerialDataReceived(byte[] RXBuffer) {
		//Dispatcher.Invoke(() => {
		//	//LogParagraph.Inlines.Add(new Run() {
		//	//	Text = Encoding.UTF8.GetString(buffer)
		//	//});

		//	//LogTextBox.ScrollToEnd();
		//});

		var text = Encoding.UTF8.GetString(RXBuffer);
		Debug.WriteLine(text);

		// Reading
		lock (AircraftPacketSyncRoot) {
			AircraftPacket = BytesToStruct<AircraftPacket>(RXBuffer);
		}

		if (AircraftPacket.Header == Packet.Header) {
			HandleReceivedPacket();
		}
		else {
			Debug.WriteLine($"RX header mismatch: {AircraftPacket.Header}");
		}

		// Writing
		PrepareSimmPacketToSend();

		var TXBuffer = new byte[AircraftPacketSize];

		lock (AircraftPacketSyncRoot) {
			StructToBytes(SimPacket, TXBuffer);
		}

		Serial.EnqueueWriting(TXBuffer);

		//Thread.Sleep(1000 / 30);
	}

	void OnPortTextBoxKeyUp(object sender, KeyEventArgs e) {
		if (e.Key is not Key.Enter)
			return;

		FocusManager.SetFocusedElement(FocusManager.GetFocusScope(TCPPortTextBox), null);
		Keyboard.ClearFocus();
	}

	void OnPortTextBoxLostFocus(object sender, RoutedEventArgs e) {
		if (!int.TryParse(TCPPortTextBox.Text, out var port))
			return;

		
	}
}