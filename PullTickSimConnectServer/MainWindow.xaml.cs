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
using System.Windows.Media.Media3D;
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

		// Autopilot
		Autopilot = new(this);

		FlightPathVectorTimer = new(
			UpdateFlightPathVector,
			null,
			FlightPathVectorInterval,
			Timeout.InfiniteTimeSpan
		);

		// Controls
		TCPPortTextBox.Text = App.Settings.port.ToString();

		Loaded += (s, e) => {
			RemoteData.AutopilotAirSpeedMs = KnotsToMetersPerSecond(90);
			RemoteData.AutopilotAutoThrottle = true;

			RemoteData.AutopilotAltitudeM = FeetToMeters(5000);
			RemoteData.AutopilotLevelChange = true;

			RemoteData.AutopilotHeadingHold = true;
			RemoteData.AutopilotHeadingRad = 0;

			Sim.Start();
			TCP.Start(App.Settings.port);

			UpdateStatus();
		};
	}

	public const float EarthEquatorialRadiusM = 6378137f;
	public const float EarchPolarRadiusM = 6356752.3142f;

	public object RemoteDataSyncRoot { get; init; } = new object();
	public object RemotePacketSyncRoot { get; init; } = new object();

	public object AircraftDataSyncRoot { get; init; } = new object();
	public object AircraftPacketSyncRoot { get; init; } = new object();

	public RemotePacket RemotePacket = new() {
		AltimeterPressurePa = 101325
	};

	public AircraftPacket AircraftPacket = new();

	public AircraftData AircraftData { get; init; } = new();
	public RemoteData RemoteData { get; init; } = new();

	public static unsafe int RemotePacketSize => sizeof(RemotePacket);
	public static unsafe int AircraftPacketSize => sizeof(AircraftPacket);

	readonly Sim Sim;
	readonly TCP TCP;
	readonly Autopilot Autopilot;

	readonly Timer FlightPathVectorTimer;
	static readonly TimeSpan FlightPathVectorInterval = TimeSpan.FromMilliseconds(250);

	Vector3D FPVPrevousCartesian = new(float.NaN, float.NaN, float.NaN);

	public void SimDataToAircraftData(SimData simData) {
		// Aircraft data
		lock (AircraftDataSyncRoot) {
			AircraftData.LatitudeRad = simData.LatitudeRad;
			AircraftData.LongitudeRad = simData.LongitudeRad;

			AircraftData.PitchRad = -simData.PitchRad;
			AircraftData.YawRad = simData.YawRad;
			AircraftData.RollRad = -simData.RollRad;

			AircraftData.AirSpeedMs = KnotsToMetersPerSecond(simData.AirSpeedKt);

			// -------------------------------- Computed--------------------------------

			// Altitude
			AircraftData.Computed.AltitudeM = PressureToAltitude(RemotePacket.AltimeterPressurePa, simData.PressureHPa * 100d);

			// Wind
			AircraftData.Computed.WindDirectionDeg = simData.WindDirectionDeg;
			AircraftData.Computed.WindSpeedMs = KnotsToMetersPerSecond(simData.WindSpeedKt);


			// -------------------------------- Slip & skid--------------------------------
			//
			// Usually, the amount of slip is expressed as a G-load in [-1; 1] range.
			// To determine it, we just need to take the lateral acceleration along
			// aircraft abeam using using accelerometer. This acceleration is formed by
			// many factors, but only 2 of them are most significant:
			//
			// The first is the centrifugal force arising from the yaw of the
			// aircraft. The greater the yaw, the greater the lateral acceleration - it's
			// simple. This is the force that interests us most, and we need to find it.

			// The second factor is the centrifugal force arising when the aircraft rolls, the
			// vector of which is always directed downwards from the bottom of the fuselage.
			// If the aircraft is leveled with the horizon, this force is equal to 0 and does
			// not affect acceleration in any way. However, the greater the bank angle, the
			// more this force will tend to 1G and the more it will affect the lateral
			// acceleration. Do you know what this looks like? A sine. Yes, the sine of the
			// bank angle multiplied by 1G, nothing more

			// So to find out the first force, take the lateral acceleration from the
			// accelerometer and subtract the second force from it:
			// 
			// g = 9.80665 m/s2
			// slipAndSkidForce = (accelerationX - sin(roll) * g) / g
			// slipAndSkidForce = accelerationX / g - sin(roll)

			const double GFtS2 = 32.1740d;
			AircraftData.Computed.SlipAndSkidG = -simData.AccelerationBodyXFt * 2f / GFtS2 + Math.Sin(AircraftData.RollRad);
		}
	}

	public void RemoteDataToSimEvents() {
		lock (RemoteData) {
			// Throttle
			if (RemoteData.AutopilotAutoThrottle) {
				Sim.SendThrottle1Event(Autopilot.Throttle);
				Sim.SendThrottle2Event(Autopilot.Throttle);
			}
			else {
				Sim.SendThrottle1Event(RemoteData.Throttle);
				Sim.SendThrottle2Event(RemoteData.Throttle);
			}

			Sim.SendElevatorEvent(RemoteData.AutopilotLevelChange ? Autopilot.Elevator : RemoteData.Elevator);
			Sim.SendAileronsEvent(1 - (RemoteData.AutopilotHeadingHold ? Autopilot.Ailerons : RemoteData.Ailerons));

			//Sim.SendRudderEvent(1 - RemoteData.Rudder);

			Sim.SendFlapsEvent(RemoteData.Flaps);
			Sim.SendSpoilersEvent(RemoteData.Spoilers);

			//Sim.SendGearSetEvent(RemoteData.LandingGear);

			Sim.SendAltimeterPressureEvent(RemoteData.AltimeterPressurePa);
		}
	}

	public void RemotePacketToRemoteData() {
		lock (RemoteDataSyncRoot) {
			lock (RemotePacketSyncRoot) {
				RemoteData.Throttle = RemotePacket.Throttle / 255d;

				RemoteData.Ailerons = RemotePacket.Ailerons / 255d;
				RemoteData.Elevator = RemotePacket.Elevator / 255d;
				RemoteData.Rudder = RemotePacket.Rudder / 255d;

				RemoteData.Flaps = RemotePacket.Flaps / 255d;
				RemoteData.Spoilers = RemotePacket.Spoilers / 255d;

				RemoteData.AltimeterPressurePa = RemotePacket.AltimeterPressurePa;

				RemoteData.AutopilotAirSpeedMs = RemotePacket.AutopilotAirspeedMs;
				RemoteData.AutopilotAutoThrottle = RemotePacket.AutopilotAutoThrottle;

				RemoteData.AutopilotHeadingRad = DegreesToRadians(RemotePacket.AutopilotHeadingDeg);
				RemoteData.AutopilotHeadingHold = RemotePacket.AutopilotHeadingHold;

				RemoteData.AutopilotAltitudeM = RemotePacket.AutopilotAltitudeM;
				RemoteData.AutopilotLevelChange = RemotePacket.AutopilotLevelChange;

				RemoteData.LandingGear = RemotePacket.LandingGear;
				RemoteData.StrobeLights = RemotePacket.StrobeLights;
			}
		}
	}

	public void AircraftDataToAircraftPacket() {
		lock (AircraftDataSyncRoot) {
			lock (AircraftPacketSyncRoot) {
				AircraftPacket.Throttle = (byte) (AircraftData.Computed.Throttle * 0xFF);

				AircraftPacket.LatitudeRad = (float) AircraftData.LatitudeRad;
				AircraftPacket.LongitudeRad = (float) AircraftData.LongitudeRad;

				AircraftPacket.PitchRad = (float) AircraftData.PitchRad;
				AircraftPacket.YawRad = (float) AircraftData.YawRad;
				AircraftPacket.RollRad = (float) AircraftData.RollRad;

				AircraftPacket.SlipAndSkid = (ushort) ((Math.Clamp(AircraftData.Computed.SlipAndSkidG, -1, 1) + 1d) / 2d * 0xFFFF);

				AircraftPacket.AltitudeM = (float) AircraftData.Computed.AltitudeM;
				AircraftPacket.AirSpeedMs = (float) AircraftData.AirSpeedMs;

				AircraftPacket.SindDirectionDeg = (ushort) AircraftData.Computed.WindDirectionDeg;
				AircraftPacket.WindSpeedMs = (float) AircraftData.Computed.WindSpeedMs;

				AircraftPacket.GroundSpeedMs = (float) AircraftData.Computed.GroundSpeedMs;

				AircraftPacket.FlightDirectorPitch = (float) AircraftData.Computed.FlightDirectorPitchRad;
				AircraftPacket.FlightDirectorYaw = (float) AircraftData.Computed.FlightDirectorRollRad;

				AircraftPacket.FlightPathPitch = (float) AircraftData.Computed.FlightPathPitchRad;
				AircraftPacket.FlightPathYaw = (float) AircraftData.Computed.FlightPathYawRad;
			}
		}
	}

	public void HandleReceivedRemotePacket() {
		RemotePacketToRemoteData();
	}

	public void PrepareAircraftPacketToSend() {
		AircraftDataToAircraftPacket();
	}

	public void UpdateFlightPathVector(object? _) {
		lock (AircraftDataSyncRoot) {
			//Debug.WriteLine($"[FPV] -------------------------------");

			//Debug.WriteLine($"[FPV] Plane LLA: {RadiansToDegrees(AircraftData.LatitudeRad)} x {RadiansToDegrees(AircraftData.LongitudeRad)} x {AircraftData.Computed.AltitudeM} m");
			//Debug.WriteLine($"[FPV] Plane PY: {RadiansToDegrees(AircraftData.PitchRad)} x {RadiansToDegrees(AircraftData.YawRad)}");

			// Cartesian
			var cartesian = GeodeticToCartesian(AircraftData.LatitudeRad, AircraftData.LongitudeRad, AircraftData.Computed.AltitudeM);

			//Debug.WriteLine($"[FPV] Cartesian: {cartesian.X} x {cartesian.Y} x {cartesian.Z}");

			Vector3D delta;

			// First call
			if (double.IsNaN(FPVPrevousCartesian.X)) {
				delta = new();
				FPVPrevousCartesian = cartesian;
			}
			else {
				delta = cartesian - FPVPrevousCartesian;
				FPVPrevousCartesian = cartesian;
			}

			//Debug.WriteLine($"[FPV] Delta: {delta.X} x {delta.Y} x {delta.Z}");

			var deltaLength = delta.Length;

			if (deltaLength > 0) {
				// Ground speed
				// deltaLength m - interval s
				// x m - 1 s
				AircraftData.Computed.GroundSpeedMs = deltaLength * 1d / FlightPathVectorInterval.TotalSeconds;
				//Debug.WriteLine($"[FPV] G/S: {AircraftData.groundSpeedMs} m/s");

				// FPV
				var rotated = delta;
				rotated = RotateAroundZAxis(rotated, -AircraftData.LongitudeRad);
				rotated = RotateAroundYAxis(rotated, -Math.PI / 2d + AircraftData.LatitudeRad);
				rotated = RotateAroundZAxis(rotated,AircraftData.YawRad);

				//Debug.WriteLine($"[FPV] Rotated: {rotated.X} x {rotated.Y} x {rotated.Z}");

				var FPVLPFFactor = 0.2;

				AircraftData.Computed.FlightPathPitchRad = LowPassFilter.Apply(
					AircraftData.Computed.FlightPathPitchRad,
					deltaLength == 0 ? 0 : Math.Asin(rotated.Z / deltaLength),
					FPVLPFFactor
				);

				AircraftData.Computed.FlightPathYawRad = LowPassFilter.Apply(
					AircraftData.Computed.FlightPathYawRad,
					deltaLength == 0 ? 0 : -Math.Atan(rotated.Y / rotated.X),
					FPVLPFFactor
				);

				//AircraftData.flightPathPitch = 20f / 180f * Math.PI;
				//AircraftData.Computed.FlightPathYawRad = 0;
			}
			else {
				AircraftData.Computed.GroundSpeedMs = 0;
				AircraftData.Computed.FlightPathPitchRad = 0;
				AircraftData.Computed.FlightPathYawRad = 0;
			}

			//Debug.WriteLine($"[FPV] FPV PY: {RadiansToDegrees(AircraftData.Computed.FlightPathPitchRad)} x {RadiansToDegrees(AircraftData.Computed.FlightPathYawRad)}");
		}

		FlightPathVectorTimer.Change(FlightPathVectorInterval, Timeout.InfiniteTimeSpan);
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

	public static Vector3D GeodeticToCartesian(double latitude, double longitude, double altitude) {
		var radius = EarthEquatorialRadiusM + altitude;
		var latCos = Math.Cos(latitude);

		return new(
			radius * latCos * Math.Cos(longitude),
			radius * latCos * Math.Sin(longitude),
			radius * Math.Sin(latitude)
		);
	}

	public static Vector3D GeodeticToECEFCartesian(double latitude, double longitude, double altitude) {
		var latCos = Math.Cos(latitude);
		var latSin = Math.Sin(latitude);

		var e2 = 1 - (EarchPolarRadiusM * EarchPolarRadiusM) / (EarthEquatorialRadiusM * EarthEquatorialRadiusM);
		var n = EarthEquatorialRadiusM / Math.Sqrt(1 - e2 * latSin * latSin);
		var h = EarthEquatorialRadiusM + altitude;

		return new(
			(n + h) * latCos * Math.Cos(longitude),
			(n + h) * latCos * Math.Sin(longitude),
			((1 - e2) * n + h) * latSin
		);
	}

	public static Point Rotate(Point vector, float angle) {
		return new(
			Math.Cos(angle) * vector.X - Math.Sin(angle) * vector.Y,
			Math.Sin(angle) * vector.X + Math.Cos(angle) * vector.Y
		);
	}

	Vector3D RotateAroundXAxis(Vector3D vector, double angle) {
		var angleSin = Math.Sin(angle);
		var angleCos = Math.Cos(angle);

		return new(
			vector.X,
			angleCos * vector.Y - angleSin * vector.Z,
			angleSin * vector.Y + angleCos * vector.Z
		);
	}

	Vector3D RotateAroundYAxis(Vector3D vector, double angle) {
		var angleSin = Math.Sin(angle);
		var angleCos = Math.Cos(angle);

		return new(
			angleCos * vector.X + angleSin * vector.Z,
			vector.Y,
			-angleSin * vector.X + angleCos * vector.Z
		);
	}

	Vector3D RotateAroundZAxis(Vector3D vector, double angle) {
		var angleSin = Math.Sin(angle);
		var angleCos = Math.Cos(angle);

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