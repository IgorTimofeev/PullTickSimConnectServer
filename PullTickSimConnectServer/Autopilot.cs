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

	public double Throttle { get; private set; } = 0;
	public double Elevator { get; private set; } = 0.5;
	public double Ailerons { get; private set; } = 0.5;

	double SpeedTrendPreviousMs = double.NaN;
	double AltitudeTrendPreviousM = double.NaN;
	double PitchTrendPreviousRad = double.NaN;

	Timer Timer;

	void Tick(object? _) {
		lock (MainWindow.AircraftDataSyncRoot) {
			lock (MainWindow.RemoteDataSyncRoot) {

				// -------------------------------- Throttle --------------------------------

				var speedTrendDeltaMs = double.IsNaN(SpeedTrendPreviousMs) ? 0 : MainWindow.AircraftData.AirSpeedMs - SpeedTrendPreviousMs;
				SpeedTrendPreviousMs = MainWindow.AircraftData.AirSpeedMs;

				var speedTrendIntervalS = 3d;
				var speedTrendPredictedDeltaMs = speedTrendIntervalS * speedTrendDeltaMs / TickInterval.TotalSeconds;
				var speedTrendPredictedMs = MainWindow.AircraftData.AirSpeedMs + speedTrendPredictedDeltaMs;

				var speedDeltaMs = MainWindow.RemoteData.AutopilotAirSpeedMs - speedTrendPredictedMs;
				var speedDeltaSmoothingMaxMs = MainWindow.KnotsToMetersPerSecond(20);

				var throttleLPFFactorMin = 0.002;
				var throttleLPFFactorMax = 0.05;
				var throttleLPFFactorLERPFactor = Math.Min(Math.Abs(speedDeltaMs) / speedDeltaSmoothingMaxMs, 1);

				Throttle = LowPassFilter.Apply(
					Throttle,
					speedDeltaMs > 0 ? 1 : 0,
					throttleLPFFactorMin + (throttleLPFFactorMax - throttleLPFFactorMin) * throttleLPFFactorLERPFactor
				);

				MainWindow.AircraftData.Computed.Throttle = Throttle;

				// -------------------------------- Pitch --------------------------------

				// Altitude
				var altitudeTrendDeltaM = double.IsNaN(AltitudeTrendPreviousM) ? 0 : MainWindow.AircraftData.Computed.AltitudeM - AltitudeTrendPreviousM;
				AltitudeTrendPreviousM = MainWindow.AircraftData.Computed.AltitudeM;

				var altitudeTrendIntervalS = 2d;
				var altitudeTrendPredictedDeltaM = altitudeTrendIntervalS * altitudeTrendDeltaM / TickInterval.TotalSeconds;
				var altitudeTrendPredictedM = MainWindow.AircraftData.Computed.AltitudeM + altitudeTrendPredictedDeltaM;

				var altitudedDeltaM = MainWindow.RemoteData.AutopilotAltitudeM - altitudeTrendPredictedM;
				var altitudedDeltaSmoothForMaxPitchM = MainWindow.FeetToMeters(10);

				// Pitch
				var pitchTrendDeltaRad = double.IsNaN(PitchTrendPreviousRad) ? 0 : MainWindow.AircraftData.PitchRad - PitchTrendPreviousRad;
				PitchTrendPreviousRad = MainWindow.AircraftData.PitchRad;

				var pitchTrendIntervalS = 3d;
				var pitchTrendPredictedDeltaRad = pitchTrendIntervalS * pitchTrendDeltaRad / TickInterval.TotalSeconds;
				var pitchTrendPredictedRad = MainWindow.AircraftData.PitchRad + pitchTrendPredictedDeltaRad;

				var pitchFactor = Math.Clamp(altitudedDeltaM / altitudedDeltaSmoothForMaxPitchM, -1, 1);
				var pitchAngleMaxRad = MainWindow.DegreesToRadians(5);
				var pitchAngleRad = pitchFactor * pitchAngleMaxRad;

				MainWindow.AircraftData.Computed.FlightDirectorPitchRad = pitchAngleRad;

				var pitchAngleDeltaRad = pitchAngleRad - pitchTrendPredictedRad;
				var pitchAngleSmoothingRad = MainWindow.DegreesToRadians(20);

				var elevatorLPFFactorMin = 0.0001;
				var elevatorLPFFactorMax = 0.01;
				var elevatorLPFFactorLERPFactor = Math.Min(Math.Abs(pitchAngleDeltaRad) / pitchAngleSmoothingRad, 1);

				Elevator = LowPassFilter.Apply(
					Elevator,
					pitchAngleDeltaRad > 0 ? 0 : 1,
					elevatorLPFFactorMin + (elevatorLPFFactorMax - elevatorLPFFactorMin) * elevatorLPFFactorLERPFactor
				);

				// -------------------------------- Yaw --------------------------------

				Ailerons = MainWindow.RemoteData.Ailerons;
			}
		}

		Timer.Change(TickInterval, Timeout.InfiniteTimeSpan);
	}
}
