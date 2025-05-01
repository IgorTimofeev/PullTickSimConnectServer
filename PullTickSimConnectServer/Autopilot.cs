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

	double PitchRad = 0;

	double SpeedTrendPreviousMs = double.NaN;
	double AltitudeTrendPreviousM = double.NaN;
	double PitchTrendPreviousRad = double.NaN;

	readonly Timer Timer;

	void Tick(object? _) {
		lock (MainWindow.AircraftDataSyncRoot) {
			lock (MainWindow.RemoteDataSyncRoot) {
				var isFlying = MainWindow.AircraftData.AirSpeedMs > MainWindow.KnotsToMetersPerSecond(20);

				MainWindow.RemoteData.AutopilotAirSpeedMs = MainWindow.KnotsToMetersPerSecond(90);


				// -------------------------------- Trends --------------------------------

				// Speed
				var speedTrendPrevDeltaMs = double.IsNaN(SpeedTrendPreviousMs) ? 0 : MainWindow.AircraftData.AirSpeedMs - SpeedTrendPreviousMs;
				SpeedTrendPreviousMs = MainWindow.AircraftData.AirSpeedMs;

				var speedTrendIntervalS = 2d;
				var speedTrendPredictedDeltaMs = speedTrendIntervalS * speedTrendPrevDeltaMs / TickInterval.TotalSeconds;
				var speedTrendPredictedMs = MainWindow.AircraftData.AirSpeedMs + speedTrendPredictedDeltaMs;
				
				var speedDeltaMs = MainWindow.RemoteData.AutopilotAirSpeedMs - MainWindow.AircraftData.AirSpeedMs;
				var speedTrendDeltaTargetMs = MainWindow.RemoteData.AutopilotAirSpeedMs - speedTrendPredictedMs;

				// Altitude
				var altitudeTrendPrevDeltaM = double.IsNaN(AltitudeTrendPreviousM) ? 0 : MainWindow.AircraftData.Computed.AltitudeM - AltitudeTrendPreviousM;
				AltitudeTrendPreviousM = MainWindow.AircraftData.Computed.AltitudeM;

				var altitudeTrendIntervalS = 2d;
				var altitudeTrendPredictedDeltaM = altitudeTrendIntervalS * altitudeTrendPrevDeltaM / TickInterval.TotalSeconds;
				var altitudeTrendPredictedM = MainWindow.AircraftData.Computed.AltitudeM + altitudeTrendPredictedDeltaM;

				var altitudeTrendDeltaTargetM = MainWindow.RemoteData.AutopilotAltitudeM - altitudeTrendPredictedM;

				// Pitch
				var pitchTrendPrevDeltaRad = double.IsNaN(PitchTrendPreviousRad) ? 0 : MainWindow.AircraftData.PitchRad - PitchTrendPreviousRad;
				PitchTrendPreviousRad = MainWindow.AircraftData.PitchRad;

				var pitchTrendIntervalS = 1d;
				var pitchTrendPredictedDeltaRad = pitchTrendIntervalS * pitchTrendPrevDeltaRad / TickInterval.TotalSeconds;
				var pitchTrendPredictedRad = MainWindow.AircraftData.PitchRad + pitchTrendPredictedDeltaRad;
		
				// -------------------------------- Throttle --------------------------------

				var throttleTargetFactorAltitudeDeltaMaxM = MainWindow.FeetToMeters(20);
				var throttleTargetDerateFactor = 1.0;

				var throttleTargetFactor =
					(
						// Climbing
						altitudeTrendDeltaTargetM > throttleTargetFactorAltitudeDeltaMaxM
						? 1.0
						: (
							// Descending
							altitudeTrendDeltaTargetM < -throttleTargetFactorAltitudeDeltaMaxM
							? 0
							// Autothrottle
							: (
								speedTrendDeltaTargetMs > 0
								? 1.0
								: 0.0
							)
						)
					)
					* throttleTargetDerateFactor;

				var throttleTargetLPFFactorMin = 0.002;
				var throttleTargetLPFFactorMax = 0.05;
				var throttleTargetLPFFactorMaxFactor = Math.Min(Math.Abs(speedTrendDeltaTargetMs) / throttleTargetFactorAltitudeDeltaMaxM, 1);
				var throttleTargetLPFFactor = throttleTargetLPFFactorMin + (throttleTargetLPFFactorMax - throttleTargetLPFFactorMin) * throttleTargetLPFFactorMaxFactor;

				Throttle = LowPassFilter.Apply(
					Throttle,
					throttleTargetFactor,
					throttleTargetLPFFactor
				);

				MainWindow.AircraftData.Computed.Throttle = Throttle;

				// -------------------------------- Target pitch --------------------------------

				var pitchTargetClimbRad = MainWindow.DegreesToRadians(10);
				var pitchTargetDescentRad = MainWindow.DegreesToRadians(10);

				var pitchTargetRad =
					isFlying
					? (speedTrendDeltaTargetMs > 0 ? -pitchTargetClimbRad : pitchTargetDescentRad)
					: 0;

				var pitchTargetTrendDeltaRad = pitchTargetRad - pitchTrendPredictedRad;

				var pitchTargetLPFFactorMin = 0.001;
				var pitchTargetLPFFactorMax = 0.05;

				var pitchTargetLPFFactorMaxPitchDeltaRad = MainWindow.DegreesToRadians(20);
				var pitchTargetLPFFactorMaxSpeedDeltaMs = MainWindow.KnotsToMetersPerSecond(10);
				var pitchTargetLPFFactorMaxAltitudeDeltaM = MainWindow.FeetToMeters(1000);
		
				var pitchTargetLPFFactorMaxFactorPitchFactor = Math.Min(Math.Abs(pitchTargetTrendDeltaRad) / pitchTargetLPFFactorMaxPitchDeltaRad, 1);
				var pitchTargetLPFFactorMaxFactorSpeedFactor = Math.Min(Math.Abs(speedTrendDeltaTargetMs) / pitchTargetLPFFactorMaxSpeedDeltaMs, 1);
				var pitchTargetLPFFactorMaxFactorAltitudeFactor = Math.Min(Math.Abs(altitudeTrendDeltaTargetM) / pitchTargetLPFFactorMaxAltitudeDeltaM, 1);

				var pitchTargetLPFFactor =
					pitchTargetLPFFactorMin
					+ (pitchTargetLPFFactorMax - pitchTargetLPFFactorMin)
					* pitchTargetLPFFactorMaxFactorPitchFactor
					* pitchTargetLPFFactorMaxFactorSpeedFactor
					* pitchTargetLPFFactorMaxFactorAltitudeFactor;

				PitchRad = LowPassFilter.Apply(
					PitchRad,
					pitchTargetRad,
					pitchTargetLPFFactor
				);

				MainWindow.AircraftData.Computed.FlightDirectorPitchRad = PitchRad;

				// -------------------------------- Elevator --------------------------------

				var elevatorPitchDeltaRad = PitchRad - pitchTrendPredictedRad;
				var elevatorPitchDeltaSmoothingRad = MainWindow.DegreesToRadians(20);

				// Elevator
				var elevatorLPFFactorMin = 0.0001;
				var elevatorLPFFactorMax = 0.02;
				var elevatorLPFFactorMaxFactor = Math.Min(Math.Abs(elevatorPitchDeltaRad) / elevatorPitchDeltaSmoothingRad, 1);

				Elevator = LowPassFilter.Apply(
					Elevator,
					elevatorPitchDeltaRad > 0 ? 0 : 1,
					elevatorLPFFactorMin + (elevatorLPFFactorMax - elevatorLPFFactorMin) * elevatorLPFFactorMaxFactor
				);

				//Debug.WriteLine($"Elevator: {Elevator:N10}");

				// -------------------------------- Ailerons --------------------------------


			}
		}

		Timer.Change(TickInterval, Timeout.InfiniteTimeSpan);
	}
}
