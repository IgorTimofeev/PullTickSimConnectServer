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

		new Thread(Tick) {
			Name = "Autopilot thread",
			IsBackground = true
		}.Start();
	}

	MainWindow MainWindow { get; init; }

	static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(100);

	double ThrottleDeltaMax = 0.2;
	LowPassInterpolator ThrottleInterpolator = new(0.08);
	public double Throttle => ThrottleInterpolator.Value;

	double PitchDeltaMaxM = 30;
	double PitchMaxRad = MainWindow.DegreesToRadians(5);

	LowPassInterpolator ElevatorInterpolator = new(0.08);

	double ElevatorFactorMax = 0.8d;
	public double Elevator => ElevatorInterpolator.Value;

	//double YawMax = 20;
	//LowPassInterpolator YawAngleInterpolator = new();
	//double YawInterpolationFactor = 0.5;
	public double Ailerons { get; private set; } = 0;

	void Tick() {
		while (true) {
			lock (MainWindow.AircraftDataSyncRoot) {
				lock (MainWindow.RemoteDataSyncRoot) {
					// Throttle
					if (MainWindow.AircraftData.AirSpeedMs >= MainWindow.RemoteData.AutopilotAirSpeedMs) {
						ThrottleInterpolator.TargetValue = 0;
					}
					else {
						ThrottleInterpolator.TargetValue = 1;
					}

					ThrottleInterpolator.Tick();

					MainWindow.AircraftData.Computed.Throttle = ThrottleInterpolator.Value;

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

			Thread.Sleep(TickInterval);
		}
	}
}
