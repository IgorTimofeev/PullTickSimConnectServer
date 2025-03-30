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

	public const float EarthEquatorialRadiusM = 6378137f;
	public const float EarchPolarRadiusM = 6356752.3142f;

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

		Sim.SendAltimeterPressureEvent(RemotePacket.altimeterPressurePa);

		Sim.SendAPSpeedEvent((uint) MetersPerSecondToKnots(RemotePacket.autopilotAirspeedMs));
		Sim.SendAutoThrottleEvent(RemotePacket.autopilotAutoThrottle);

		Sim.SendAPHeadingEvent(RemotePacket.autopilotHeadingDeg);
		Sim.SendAPHDGHoldEvent(RemotePacket.autopilotHeadingHold);

		Sim.SendAPAltitudeEvent((uint) MetersToFeet(RemotePacket.autopilotAltitudeM));
		Sim.SendAPLevelChangeEvent(RemotePacket.autopilotLevelChange);
	}

	public void HandleAircraftPacket(SimData simData) {
		lock (PacketsSyncRoot) {
			AircraftPacket.latitudeRad = (float) simData.LatitudeRad;
			AircraftPacket.longitudeRad = (float) simData.LongitudeRad;

			AircraftPacket.pitchRad = (float) -simData.PitchRad;
			AircraftPacket.yawRad = (float) simData.YawRad;
			AircraftPacket.rollRad = (float) -simData.RollRad;
			AircraftPacket.slipAndSkid = (short) (simData.SlipAndSkid / 127d * (0xFFFF / 2d));

			AircraftPacket.altitudeM = (float) PressureToAltitude(RemotePacket.altimeterPressurePa, simData.PressureHPa * 100d);
			AircraftPacket.airSpeedMs = KnotsToMetersPerSecond((float) simData.AirSpeedKt);

			AircraftPacket.windDirectionDeg = (ushort) simData.WindDirectionDeg;
			AircraftPacket.windSpeedMs = KnotsToMetersPerSecond((float) simData.WindSpeedKt);

			// Pressure

			//AircraftPacket.latitudeRad -= (59f / 180f * MathF.PI);
			//AircraftPacket.longitudeRad = 0;
			//AircraftPacket.yawRad = 0;
			//AircraftPacket.altitudeM = 5000;
		}
	}

	static double PressureToAltitude(double referencePressurePa, double pressurePa) {
		const double T0 = 288.15;
		const double L = 0.0065;
		const double RS = 287.058;
		const double g = 9.80665;

		return T0 / L * (1 - Math.Pow(pressurePa / referencePressurePa, RS * L / g));
	}

	public void UpdateFlightPathVector(object? _) {
		var interval = 0.5f;

		lock (PacketsSyncRoot) {
			Debug.WriteLine($"[FPV] -------------------------------");

			Debug.WriteLine($"[FPV] Plane LLA: {RadiansToDegrees(AircraftPacket.latitudeRad)} x {RadiansToDegrees(AircraftPacket.longitudeRad)} x {AircraftPacket.altitudeM} m");
			Debug.WriteLine($"[FPV] Plane PY: {RadiansToDegrees(AircraftPacket.pitchRad)} x {RadiansToDegrees(AircraftPacket.yawRad)}");

			var cartesian = GeodeticToCartesian(AircraftPacket.latitudeRad, AircraftPacket.longitudeRad, AircraftPacket.altitudeM);

			// Cartesian
			Debug.WriteLine($"[FPV] Cartesian: {cartesian.X} x {cartesian.Y} x {cartesian.Z}");

			var delta = cartesian - OldFPVCartesian;
			OldFPVCartesian = cartesian;

			Debug.WriteLine($"[FPV] Delta: {delta.X} x {delta.Y} x {delta.Z}");

			var deltaLength = delta.Length();

			if (deltaLength > 0) {
				// Ground speed
				// deltaLength m - interval s
				// x m - 1 s
				AircraftPacket.groundSpeedMs = deltaLength * 1f / interval;
				Debug.WriteLine($"[FPV] G/S: {AircraftPacket.groundSpeedMs} m/s");

				// FPV
				var rotated = delta;
				rotated = RotateAroundZAxis(rotated, -AircraftPacket.longitudeRad);
				rotated = RotateAroundYAxis(rotated, -MathF.PI / 2f + AircraftPacket.latitudeRad);
				rotated = RotateAroundZAxis(rotated, AircraftPacket.yawRad);

				Debug.WriteLine($"[FPV] Rotated: {rotated.X} x {rotated.Y} x {rotated.Z}");

				AircraftPacket.flightPathPitch = deltaLength == 0 ? 0 : MathF.Asin(rotated.Z / deltaLength);
				AircraftPacket.flightPathYaw = deltaLength == 0 ? 0 : -MathF.Atan(rotated.Y / rotated.X);

				//AircraftPacket.flightPathPitch = 20f / 180f * MathF.PI;
				//AircraftPacket.flightPathYaw = 0;
			}
			else {
				AircraftPacket.groundSpeedMs = 0;
				AircraftPacket.flightPathPitch = 0;
				AircraftPacket.flightPathYaw = 0;
			}

			Debug.WriteLine($"[FPV] FPV PY: {RadiansToDegrees(AircraftPacket.flightPathPitch)} x {RadiansToDegrees(AircraftPacket.flightPathYaw)}");
		}

		FlightPathVectorTimer.Change(TimeSpan.FromSeconds(interval), Timeout.InfiniteTimeSpan);
	}

	public static float RadiansToDegrees(float radians) => radians / MathF.PI * 180f;
	public static float DegreesToRadians(float degrees) => degrees / 180f * MathF.PI;

	public static float FeetToMeters(float feet) => feet * 0.3048f;
	public static float MetersToFeet(float meters) => meters / 0.3048f;

	public static float KnotsToMetersPerSecond(float knots) => knots * 0.5144444444f;
	public static float MetersPerSecondToKnots(float metersPerSecond) => metersPerSecond / 0.5144444444f;

	public static Vector3 GeodeticToCartesian(float latitude, float longitude, float altitude) {
		var radius = EarthEquatorialRadiusM + altitude;
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

		var e2 = 1 - (EarchPolarRadiusM * EarchPolarRadiusM) / (EarthEquatorialRadiusM * EarthEquatorialRadiusM);
		var n = EarthEquatorialRadiusM / MathF.Sqrt(1 - e2 * latSin * latSin);
		var h = EarthEquatorialRadiusM + altitude;

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