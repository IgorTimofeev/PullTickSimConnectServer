using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PullTickSimConnectServer;

public partial class MainWindow : Window {
	public MainWindow() {
		InitializeComponent();

		// Sim
		Sim = new(this);
		Sim.IsConnectedChanged += UpdateStatus;

		// TCP
		TCP = new(this);
		TCP.IsStartedChanged += UpdateStatus;

		TCPPortTextBox.Text = App.Settings.port.ToString();

		// Initialization
		FlightPathVectorTimer = new(
			 UpdateFlightPathVector,
			 null,
			 TimeSpan.FromSeconds(1),
			 Timeout.InfiniteTimeSpan
		);

		Loaded += (s, e) => {
			Sim.Start();
			TCP.Start(App.Settings.port);
			UpdateStatus();
		};
	}

	public object PacketsSyncRoot { get; init; } = new object();

	public RemotePacket RemotePacket;
	public AircraftPacket AircraftPacket;

	public static unsafe int RemotePacketSize => sizeof(RemotePacket);
	public static unsafe int AircraftPacketSize => sizeof(AircraftPacket);

	Timer FlightPathVectorTimer;

	readonly Sim Sim;
	readonly TCP TCP;

	Vector3 OldFPVCartesian = new();

	public void HandleRemotePacket() {
		if (!Sim.IsConnected)
			return;

		Sim.SendThrottle1Event(RemotePacket.throttle1);
		Sim.SendThrottle2Event(RemotePacket.throttle2);

		Sim.SendAileronsEvent((ushort) (0xFFFF - RemotePacket.ailerons));
		Sim.SendElevatorEvent(RemotePacket.elevator);
		Sim.SendRudderEvent((ushort) (0xFFFF - RemotePacket.rudder));

		Sim.SendFlapsEvent(RemotePacket.flaps);
		Sim.SendSpoilersEvent(RemotePacket.spoilers);

		Sim.SendGearSetEvent(RemotePacket.landingGear);
	}

	public void HandleAircraftPacket(SimData simData) {
		lock (PacketsSyncRoot) {
			AircraftPacket.latitude = (float) simData.Latitude;
			AircraftPacket.longitude = (float) simData.Longitude;

			AircraftPacket.pitch = (float) -simData.Pitch;
			AircraftPacket.yaw = (float) simData.Yaw;
			AircraftPacket.roll = (float) -simData.Roll;
			AircraftPacket.slipAndSkid = (float) simData.SlipAndSkid / 127f;

			AircraftPacket.altitude = (float) simData.Altitude;
			AircraftPacket.speed = (float) simData.Speed;

			AircraftPacket.pressure = (float) simData.Pressure;
			AircraftPacket.temperature = (float) simData.Temperature;

			//AircraftPacket.latitude -= (59f / 180f * MathF.PI);
			//AircraftPacket.longitude = 0;
			//AircraftPacket.yaw = 0;
			//AircraftPacket.altitude = 5000;
		}
	}

	public void UpdateFlightPathVector(object? _) {
		lock (PacketsSyncRoot) {
			Debug.WriteLine($"[FPV] -------------------------------");

			Debug.WriteLine($"[FPV] Plane LL: {AircraftPacket.latitude / MathF.PI * 180f} x {AircraftPacket.longitude / MathF.PI * 180f}");
			Debug.WriteLine($"[FPV] Plane PY: {AircraftPacket.pitch / MathF.PI * 180f} x {AircraftPacket.yaw / MathF.PI * 180f}");

			GeocentricCoordinates geocentric = new(AircraftPacket.latitude, AircraftPacket.longitude, AircraftPacket.altitude / 3.2808399f);

			var cartesian = geocentric.ToCartesian();

			// Cartesian
			AircraftPacket.x = cartesian.X;
			AircraftPacket.y = cartesian.Y;
			AircraftPacket.z = cartesian.Z;

			Debug.WriteLine($"[FPV] Cartesian: {cartesian.X} x {cartesian.Y} x {cartesian.Z}");

			// FPV
			var delta = cartesian - OldFPVCartesian;
			OldFPVCartesian = cartesian;

			Debug.WriteLine($"[FPV] Delta: {delta.X} x {delta.Y} x {delta.Z}");

			var rotated = delta;
			rotated = rotateAroundZAxis(rotated, -AircraftPacket.longitude);
			rotated = rotateAroundYAxis(rotated, -MathF.PI / 2f + AircraftPacket.latitude);
			rotated = rotateAroundZAxis(rotated, AircraftPacket.yaw);

			Debug.WriteLine($"[FPV] Rotated: {rotated.X} x {rotated.Y} x {rotated.Z}");

			var len = rotated.Length();
			AircraftPacket.flightPathPitch = len == 0 ? 0 : MathF.Asin(rotated.Z / len);
			AircraftPacket.flightPathYaw = len == 0 ? 0 : -MathF.Atan(rotated.Y / rotated.X);

			//AircraftPacket.flightPathPitch = 20f / 180f * MathF.PI;
			//AircraftPacket.flightPathYaw = 0;

			Debug.WriteLine($"[FPV] FPV PY: {AircraftPacket.flightPathPitch / MathF.PI * 180f} x {AircraftPacket.flightPathYaw / MathF.PI * 180f}");
		}

		FlightPathVectorTimer.Change(TimeSpan.FromMilliseconds(1000), Timeout.InfiniteTimeSpan);
	}

	Vector3 rotateAroundXAxis(Vector3 vector, float angle) {
		var angleSin = MathF.Sin(angle);
		var angleCos = MathF.Cos(angle);

		return new(
			vector.X,
			angleCos * vector.Y - angleSin * vector.Z,
			angleSin * vector.Y + angleCos * vector.Z
		);
	}

	Vector3 rotateAroundYAxis(Vector3 vector, float angle) {
		var angleSin = MathF.Sin(angle);
		var angleCos = MathF.Cos(angle);

		return new(
			angleCos * vector.X + angleSin * vector.Z,
			vector.Y,
			-angleSin * vector.X + angleCos * vector.Z
		);
	}

	Vector3 rotateAroundZAxis(Vector3 vector, float angle) {
		var angleSin = MathF.Sin(angle);
		var angleCos = MathF.Cos(angle);

		return new(
			angleCos * vector.X - angleSin * vector.Y,
			angleSin * vector.X + angleCos * vector.Y,
			vector.Z
		);
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
		var state = TCP.IsStarted && Sim.IsConnected;

		StatusEllipse.Stroke = (SolidColorBrush) Application.Current.Resources[
			state
			? "ThemeAccent1"
			: "ThemeAccent3"
		];

		StatusEffect.Opacity = state ? 0.4 : 0;

		StatusImage.Source = new BitmapImage(new Uri($"Resources/Images/{(state ? "LogoOn" : "LogoOff")}.png", UriKind.Relative));

		BackgroundRectangle.StrokeThickness = state ? 1 : 0;

		TCPStatusRun.Text = TCP.IsStarted ? "running" : "stopped";
		SimStatusRun.Text = Sim.IsConnected ? "running" : "not found";
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

		if (TCP.IsStarted) {
			if (port == App.Settings.port) {
				return;
			}
			else {
				TCP.Stop();
			}
		}

		App.Settings.port = port;

		TCP.Start(App.Settings.port);
	}
}