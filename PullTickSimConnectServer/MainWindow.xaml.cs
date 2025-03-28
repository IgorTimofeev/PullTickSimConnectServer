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

	const float EarthEquatorialRadius = 6378137f;
	const float EarchPolarRadius = 6356752.3142f;

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
			AircraftPacket.airSpeed = (float) simData.AirSpeed;

			AircraftPacket.pressure = (float) simData.Pressure;
			AircraftPacket.temperature = (float) simData.Temperature;

			AircraftPacket.windDirection = (float) simData.WindDirectionDegrees / 180f * MathF.PI;
			AircraftPacket.windSpeed = (float) simData.WindSpeedKnots;

			//AircraftPacket.latitude -= (59f / 180f * MathF.PI);
			//AircraftPacket.longitude = 0;
			//AircraftPacket.yaw = 0;
			//AircraftPacket.altitude = 5000;
		}
	}

	public void UpdateFlightPathVector(object? _) {
		var interval = 1.0f;

		lock (PacketsSyncRoot) {
			Debug.WriteLine($"[FPV] -------------------------------");

			Debug.WriteLine($"[FPV] Plane LLA: {AircraftPacket.latitude / MathF.PI * 180f} x {AircraftPacket.longitude / MathF.PI * 180f} x {AircraftPacket.altitude} ft");
			Debug.WriteLine($"[FPV] Plane PY: {AircraftPacket.pitch / MathF.PI * 180f} x {AircraftPacket.yaw / MathF.PI * 180f}");

			var cartesian = GeodeticToCartesian(AircraftPacket.latitude, AircraftPacket.longitude, AircraftPacket.altitude * 0.3048f);

			// Cartesian
			AircraftPacket.x = cartesian.X;
			AircraftPacket.y = cartesian.Y;
			AircraftPacket.z = cartesian.Z;

			Debug.WriteLine($"[FPV] Cartesian: {cartesian.X} x {cartesian.Y} x {cartesian.Z}");

			var delta = cartesian - OldFPVCartesian;
			OldFPVCartesian = cartesian;

			Debug.WriteLine($"[FPV] Delta: {delta.X} x {delta.Y} x {delta.Z}");

			var deltaLength = delta.Length();

			if (deltaLength > 0) {
				// Ground speed
				// deltaLength m - interval s
				// x m - 1 s

				// M/S
				AircraftPacket.groundSpeed = deltaLength * 1f / interval;
				Debug.WriteLine($"[FPV] G/S: {AircraftPacket.groundSpeed} m/s");

				// Knots
				AircraftPacket.groundSpeed /= 0.5144444444f;
				Debug.WriteLine($"[FPV] G/S: {AircraftPacket.groundSpeed} kt");

				// FPV
				var rotated = delta;
				rotated = RotateAroundZAxis(rotated, -AircraftPacket.longitude);
				rotated = RotateAroundYAxis(rotated, -MathF.PI / 2f + AircraftPacket.latitude);
				rotated = RotateAroundZAxis(rotated, AircraftPacket.yaw);

				Debug.WriteLine($"[FPV] Rotated: {rotated.X} x {rotated.Y} x {rotated.Z}");

				AircraftPacket.flightPathPitch = deltaLength == 0 ? 0 : MathF.Asin(rotated.Z / deltaLength);
				AircraftPacket.flightPathYaw = deltaLength == 0 ? 0 : -MathF.Atan(rotated.Y / rotated.X);

				//AircraftPacket.flightPathPitch = 20f / 180f * MathF.PI;
				//AircraftPacket.flightPathYaw = 0;
			}
			else {
				AircraftPacket.groundSpeed = 0;
				AircraftPacket.flightPathPitch = 0;
				AircraftPacket.flightPathYaw = 0;
			}

			Debug.WriteLine($"[FPV] FPV PY: {AircraftPacket.flightPathPitch / MathF.PI * 180f} x {AircraftPacket.flightPathYaw / MathF.PI * 180f}");
		}

		FlightPathVectorTimer.Change(TimeSpan.FromSeconds(interval), Timeout.InfiniteTimeSpan);
	}

	public static Vector3 GeodeticToCartesian(float latitude, float longitude, float altitude) {
		var radius = EarthEquatorialRadius + altitude;
		var latCos = MathF.Cos(latitude);

		return new(
			radius * latCos * MathF.Cos(longitude),
			radius * latCos * MathF.Sin(longitude),
			radius * MathF.Sin(latitude)
		);
	}

	public static Vector3 GeodeticToECEFCartesian(float latitude, float longitude, float altitude) {
		var latCos = MathF.Cos(latitude);
		var latSin = MathF.Sin(latitude);

		var e2 = 1 - (EarchPolarRadius * EarchPolarRadius) / (EarthEquatorialRadius * EarthEquatorialRadius);
		var n = EarthEquatorialRadius / MathF.Sqrt(1 - e2 * latSin * latSin);
		var h = EarthEquatorialRadius + altitude;

		return new(
			(n + h) * latCos * MathF.Cos(longitude),
			(n + h) * latCos * MathF.Sin(longitude),
			((1 - e2) * n + h) * latSin
		);
	}

	Vector3 RotateAroundXAxis(Vector3 vector, float angle) {
		var angleSin = MathF.Sin(angle);
		var angleCos = MathF.Cos(angle);

		return new(
			vector.X,
			angleCos * vector.Y - angleSin * vector.Z,
			angleSin * vector.Y + angleCos * vector.Z
		);
	}

	Vector3 RotateAroundYAxis(Vector3 vector, float angle) {
		var angleSin = MathF.Sin(angle);
		var angleCos = MathF.Cos(angle);

		return new(
			angleCos * vector.X + angleSin * vector.Z,
			vector.Y,
			-angleSin * vector.X + angleCos * vector.Z
		);
	}

	Vector3 RotateAroundZAxis(Vector3 vector, float angle) {
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