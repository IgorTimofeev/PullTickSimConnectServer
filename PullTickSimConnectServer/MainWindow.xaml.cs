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

public class GeocentricCoordinates(float latitude, float longitude, float altitude) {
	public GeocentricCoordinates() : this(0, 0, 0) {

	}

	public const float EquatorialRadius = 6378137;

	public float Latitude { get; set; } = latitude;
	public float Longitude { get; set; } = longitude;
	public float Altitude { get; set; } = altitude;

	public Vector3 ToCartesian() {
		var radius = EquatorialRadius + Altitude;
		var latCos = MathF.Cos(Latitude);

		return new(
			radius * latCos * MathF.Cos(Longitude),
			radius * latCos * MathF.Sin(Longitude),
			radius * MathF.Sin(Latitude)
		);
	}

	public override string ToString() => $"{Latitude} x {Longitude} x {Altitude}";
}

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

	Vector3 OldFlightPathVectorCartesian = new();

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

			//AircraftPacket.longitude = 0;

			AircraftPacket.pitch = (float) -simData.Pitch;
			AircraftPacket.yaw = (float) simData.Yaw;
			AircraftPacket.roll = (float) -simData.Roll;
			AircraftPacket.slipAndSkid = (float) simData.SlipAndSkid / 127f;

			AircraftPacket.altitude = (float) simData.Altitude;
			AircraftPacket.speed = (float) simData.Speed;

			AircraftPacket.pressure = (float) simData.Pressure;
			AircraftPacket.temperature = (float) simData.Temperature;
		}
	}


	public void UpdateFlightPathVector(object? _) {
		lock (PacketsSyncRoot) {
			Debug.WriteLine($"[FPV] -------------------------------");

			Debug.WriteLine($"[FPV] Plane PYR: {AircraftPacket.pitch * 180f / MathF.PI} x {AircraftPacket.yaw * 180f / MathF.PI} x {AircraftPacket.roll * 180f / MathF.PI}");

			GeocentricCoordinates geocentric = new(AircraftPacket.latitude, AircraftPacket.longitude, AircraftPacket.altitude);

			Debug.WriteLine($"[FPV] Geocentric: {geocentric}");

			var cartesian = geocentric.ToCartesian();

			// Cartesian
			AircraftPacket.x = cartesian.X;
			AircraftPacket.y = cartesian.Y;
			AircraftPacket.z = cartesian.Z;

			// FPV
			var cartesianDelta = cartesian - OldFlightPathVectorCartesian;
			OldFlightPathVectorCartesian = cartesian;

			Debug.WriteLine($"[FPV] Delta: {cartesianDelta.X} x {cartesianDelta.Y} x {cartesianDelta.Z}");

			// Transforming earth-based coordinate system to aircraft-based
			Vector3 rotated = cartesianDelta;
			rotated = rotateAroundZAxis(rotated, -AircraftPacket.longitude);
			rotated = rotateAroundXAxis(rotated, -AircraftPacket.latitude + (90f / 180f * MathF.PI) );

			Debug.WriteLine($"[FPV] Rotated: {rotated.X} x {rotated.Y} x {rotated.Z}");

			var rotatedLength = rotated.Length();

			AircraftPacket.flightPathPitch = rotatedLength == 0 ? 0 : MathF.Asin(rotated.Z / rotatedLength);
			AircraftPacket.flightPathYaw = MathF.Atan2(rotated.Y, rotated.X);

			//AircraftPacket.flightPathYaw = 0;

			Debug.WriteLine($"[FPV] Result PY: {AircraftPacket.flightPathPitch / MathF.PI * 180f} x {AircraftPacket.flightPathYaw / MathF.PI * 180f}");
		}

		FlightPathVectorTimer.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
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
			-angleSin * vector.Z + angleCos * vector.Z
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