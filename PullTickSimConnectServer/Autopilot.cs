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

	readonly Timer Timer;

	void Tick(object? _) {
		lock (MainWindow.AircraftDataSyncRoot) {
			lock (MainWindow.RemoteDataSyncRoot) {

				MainWindow.RemoteData.AutopilotAirSpeedMs = MainWindow.KnotsToMetersPerSecond(90);

				// -------------------------------- Throttle --------------------------------

				// Speed
				var speedTrendDeltaMs = double.IsNaN(SpeedTrendPreviousMs) ? 0 : MainWindow.AircraftData.AirSpeedMs - SpeedTrendPreviousMs;
				SpeedTrendPreviousMs = MainWindow.AircraftData.AirSpeedMs;

				var speedTrendIntervalS = 3d;
				var speedTrendPredictedDeltaMs = speedTrendIntervalS * speedTrendDeltaMs / TickInterval.TotalSeconds;
				var speedTrendPredictedMs = MainWindow.AircraftData.AirSpeedMs + speedTrendPredictedDeltaMs;

				var speedDeltaMs = MainWindow.RemoteData.AutopilotAirSpeedMs - speedTrendPredictedMs;
				var speedDeltaSmoothingMaxMs = MainWindow.KnotsToMetersPerSecond(20);
				var speedDeltaFactor = Math.Min(Math.Abs(speedDeltaMs) / speedDeltaSmoothingMaxMs, 1);

				// Throttle
				var throttleLPFFactorMin = 0.002;
				var throttleLPFFactorMax = 0.05;

				Throttle = LowPassFilter.Apply(
					Throttle,
					speedDeltaMs > 0 ? 1 : 0,
					throttleLPFFactorMin + (throttleLPFFactorMax - throttleLPFFactorMin) * speedDeltaFactor
				);

				MainWindow.AircraftData.Computed.Throttle = Throttle;

				// -------------------------------- Elevator --------------------------------

				// Altitude
				var altitudeTrendDeltaM = double.IsNaN(AltitudeTrendPreviousM) ? 0 : MainWindow.AircraftData.Computed.AltitudeM - AltitudeTrendPreviousM;
				AltitudeTrendPreviousM = MainWindow.AircraftData.Computed.AltitudeM;

				var altitudeTrendIntervalS = 2d;
				var altitudeTrendPredictedDeltaM = altitudeTrendIntervalS * altitudeTrendDeltaM / TickInterval.TotalSeconds;
				var altitudeTrendPredictedM = MainWindow.AircraftData.Computed.AltitudeM + altitudeTrendPredictedDeltaM;

				var altitudedDeltaM = MainWindow.RemoteData.AutopilotAltitudeM - altitudeTrendPredictedM;
				var altitudedDeltaSmoothForPitchAngleMaxM = MainWindow.FeetToMeters(10);

				// Pitch
				var pitchTrendDeltaRad = double.IsNaN(PitchTrendPreviousRad) ? 0 : MainWindow.AircraftData.PitchRad - PitchTrendPreviousRad;
				PitchTrendPreviousRad = MainWindow.AircraftData.PitchRad;

				var pitchTrendIntervalS = 2d;
				var pitchTrendPredictedDeltaRad = pitchTrendIntervalS * pitchTrendDeltaRad / TickInterval.TotalSeconds;
				var pitchTrendPredictedRad = MainWindow.AircraftData.PitchRad + pitchTrendPredictedDeltaRad;

				double pitchRad;

				var pitchSpeedDeltaClimbMargin = MainWindow.KnotsToMetersPerSecond(5);

				if (altitudedDeltaM > 0 && speedDeltaMs > pitchSpeedDeltaClimbMargin) {
					pitchRad = MainWindow.DegreesToRadians(1);
				}
				else {
					var pitchMinRad = MainWindow.DegreesToRadians(5);
					var pitchMaxRad = MainWindow.DegreesToRadians(10);
					var pitchFactor = Math.Clamp(altitudedDeltaM / altitudedDeltaSmoothForPitchAngleMaxM, -1, 1);
					var pitchMaxFactor = speedDeltaMs < -pitchSpeedDeltaClimbMargin ? 1 : speedDeltaFactor;
					pitchRad = (pitchMinRad + (pitchMaxRad - pitchMinRad) * pitchMaxFactor) * pitchFactor;
				}

				MainWindow.AircraftData.Computed.FlightDirectorPitchRad = pitchRad;

				var pitchDeltaRad = pitchRad - pitchTrendPredictedRad;
				var pitchDeltaSmoothingRad = MainWindow.DegreesToRadians(20);
				var pitchDeltaFactor = Math.Min(Math.Abs(pitchDeltaRad) / pitchDeltaSmoothingRad, 1);

				// Elevator
				var elevatorLPFFactorMin = 0.0001;
				var elevatorLPFFactorMax = 0.01;

				Elevator = LowPassFilter.Apply(
					Elevator,
					pitchDeltaRad > 0 ? 0 : 1,
					elevatorLPFFactorMin + (elevatorLPFFactorMax - elevatorLPFFactorMin) * pitchDeltaFactor
				);

				// -------------------------------- Ailerons --------------------------------

				Ailerons = MainWindow.RemoteData.Ailerons;
			}
		}

		Timer.Change(TickInterval, Timeout.InfiniteTimeSpan);
	}
}
