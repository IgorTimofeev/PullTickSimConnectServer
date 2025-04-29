using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PullTickSimConnectServer;

public enum AutopilotValueMode : byte {
	None,
	Start,
	End
}

public class Autopilot {
	public Autopilot(MainWindow mainWindow) {
		MainWindow = mainWindow;

		Timer = new(Tick, null, TickInterval, Timeout.InfiniteTimeSpan);
	}

	MainWindow MainWindow { get; init; }

	static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1d / 30d);

	double ThrottleDeltaMax = 0.2;
	public double Throttle { get; private set; } = 0;
	PIDController ThrottlePID = new(1.6, 0.014, 11.3, 0, 1);

	double PitchDeltaMaxM = 30;
	double PitchMaxRad = MainWindow.DegreesToRadians(5);

	LowPassInterpolator ElevatorInterpolator = new(0.02);

	double ElevatorFactorMax = 0.8d;
	public double Elevator => ElevatorInterpolator.Value;

	//double YawMax = 20;
	//LowPassInterpolator YawAngleInterpolator = new();
	//double YawInterpolationFactor = 0.5;
	public double Ailerons { get; private set; } = 0;

	Timer Timer;

	void Tick(object? _) {
		lock (MainWindow.AircraftDataSyncRoot) {
			lock (MainWindow.RemoteDataSyncRoot) {
				// Throttle
				var speedDelta = MainWindow.RemoteData.AutopilotAirSpeedMs - MainWindow.AircraftData.AirSpeedMs;
				var speedDeltaSoft = 20;
				var speedDeltaFactor = Math.Min(Math.Abs(speedDelta) / speedDeltaSoft, 1);
				var throttleFactor = speedDelta >= 0 ? 1 : 0;

				ThrottlePID.P = 0.001;
				ThrottlePID.I = 0.01 + speedDeltaFactor * 0.5;
				ThrottlePID.D = 0;

				Throttle = ThrottlePID.Update(Throttle, throttleFactor, TickInterval.TotalSeconds);
				MainWindow.AircraftData.Computed.Throttle = Throttle;

				// Pitch
				var pitchDeltaM = MainWindow.RemoteData.AutopilotAltitudeM - MainWindow.AircraftData.Computed.AltitudeM;
				var pitchFactor = Math.Clamp(pitchDeltaM / PitchDeltaMaxM, -1, 1);
				var pitchAngleRad = pitchFactor * PitchMaxRad;

				MainWindow.AircraftData.Computed.FlightDirectorPitchRad = pitchAngleRad;

				// Elevator
				var pitchAngleDeltaRad = pitchAngleRad - MainWindow.AircraftData.PitchRad;

				// Delta > 0 = nose up, delta < 0 = nose down
				var elevatorFactor = Math.Clamp(pitchAngleDeltaRad / PitchMaxRad, -1, 1) * ElevatorFactorMax;

				ElevatorInterpolator.TargetValue = 1d - (1d + elevatorFactor) / 2d;
				ElevatorInterpolator.Tick();

				// Yaw
				Ailerons = MainWindow.RemoteData.Ailerons;
			}
		}

		Timer.Change(TickInterval, Timeout.InfiniteTimeSpan);
	}
}
