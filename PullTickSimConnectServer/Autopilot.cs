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

	public double Throttle { get; private set; } = 0.5;
	public double Elevator { get; private set; } = 0.5;
	public double Ailerons { get; private set; } = 0.5;

	double PitchRad = 0;
	double FDRollRad = 0;

	double SpeedTrendPreviousMs = double.NaN;
	double AltitudeTrendPreviousM = double.NaN;
	double PitchTrendPreviousRad = double.NaN;
	double YawTrendPreviousRad = double.NaN;
	double RollTrendPreviousRad = double.NaN;

	readonly Timer Timer;

	void Tick(object? _) {
		lock (MainWindow.AircraftDataSyncRoot) {
			lock (MainWindow.RemoteDataSyncRoot) {
				var isFlying = MainWindow.AircraftData.AirSpeedMs > MainWindow.KnotsToMetersPerSecond(20);

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

				// Yaw
				var yawTrendPrevDeltaRad = double.IsNaN(YawTrendPreviousRad) ? 0 : MainWindow.AircraftData.YawRad - YawTrendPreviousRad;
				YawTrendPreviousRad = MainWindow.AircraftData.YawRad;

				var yawTrendIntervalS = 2d;
				var yawTrendPredictedDeltaRad = yawTrendIntervalS * yawTrendPrevDeltaRad / TickInterval.TotalSeconds;
				var yawTrendPredictedRad = MainWindow.AircraftData.YawRad + yawTrendPredictedDeltaRad;

				var yawTrendDeltaTargetRad = MainWindow.RemoteData.AutopilotHeadingRad - yawTrendPredictedRad;

				// Roll
				var rollTrendPrevDeltaRad = double.IsNaN(RollTrendPreviousRad) ? 0 : MainWindow.AircraftData.RollRad - RollTrendPreviousRad;
				RollTrendPreviousRad = MainWindow.AircraftData.RollRad;

				var rollTrendIntervalS = 1d;
				var rollTrendPredictedDeltaRad = rollTrendIntervalS * rollTrendPrevDeltaRad / TickInterval.TotalSeconds;
				var rollTrendPredictedRad = MainWindow.AircraftData.RollRad + rollTrendPredictedDeltaRad;

				// -------------------------------- Throttle --------------------------------

				var throttleTargetFactorAltitudeDeltaMaxM = MainWindow.FeetToMeters(20);
				var throttleTargetDerateFactor = 1.0;

				var throttleTargetFactor =
					(
						// Not enough speed
						speedTrendDeltaTargetMs > 0
						? 1.0
						// Enough
						: (
							// Climbing
							altitudeTrendDeltaTargetM > throttleTargetFactorAltitudeDeltaMaxM
							? 1.0
							: 0.0
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

				// -------------------------------- Pitch --------------------------------

				var pitchTargetDeltaMaxRad = MainWindow.DegreesToRadians(10);

				var pitchTargetRad =
					isFlying
					? (speedTrendDeltaTargetMs > 0 ? -pitchTargetDeltaMaxRad : pitchTargetDeltaMaxRad)
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

				MainWindow.AircraftData.Computed.FlightDirectorPitchRad = PitchRad - MainWindow.AircraftData.PitchRad;

				// -------------------------------- Elevator --------------------------------

				var elevatorPitchDeltaRad = PitchRad - pitchTrendPredictedRad;
				var elevatorPitchDeltaSmoothingRad = MainWindow.DegreesToRadians(20);

				var elevatorTargetFactor =
					isFlying
					? (elevatorPitchDeltaRad > 0 ? 0 : 1)
					: 0.5;

				var elevatorLPFFactorMin = 0.0001;
				var elevatorLPFFactorMax = 0.02;
				var elevatorLPFFactorMaxFactor = Math.Min(Math.Abs(elevatorPitchDeltaRad) / elevatorPitchDeltaSmoothingRad, 1);
				var elevatorLPFFactor = elevatorLPFFactorMin + (elevatorLPFFactorMax - elevatorLPFFactorMin) * elevatorLPFFactorMaxFactor;

				Elevator = LowPassFilter.Apply(
					Elevator,
					elevatorTargetFactor,
					elevatorLPFFactor
				);

				//Debug.WriteLine($"Elevator: {Elevator:N10}");


				// -------------------------------- Roll --------------------------------


				var rollMaxRad = MainWindow.DegreesToRadians(30);

				var yawToRight = yawTrendDeltaTargetRad > 0 && yawTrendDeltaTargetRad < Math.PI || yawTrendDeltaTargetRad < 0 && yawTrendDeltaTargetRad < -Math.PI;
				var rollToRight = yawToRight && rollTrendPredictedRad < rollMaxRad || !yawToRight && rollTrendPredictedRad < -rollMaxRad;

				var rollTargetRadYawSmoothingRad = MainWindow.DegreesToRadians(70);
				var rollTargetRadFactor = Math.Min(Math.Abs(yawTrendDeltaTargetRad) / rollTargetRadYawSmoothingRad, 1);
				var rollTargetRad = (rollToRight ? rollMaxRad : -rollMaxRad) * rollTargetRadFactor;
				var rollTargetRadPredictedDelta = rollTargetRad - rollTrendPredictedRad;

				//Debug.WriteLine($"Roll: {MainWindow.RadiansToDegrees(rollTargetRad)} deg");

				// -------------------------------- Flight director roll --------------------------------

				var FDRollTargetRad = rollTargetRad - MainWindow.AircraftData.RollRad;
				var FDRollTargetLPFFactorMin = 0.0001;
				var FDRollTargetLPFFactorMax = 0.005;
				var FDRollTargetLPFFactorRollDeltaSmoothingRad = MainWindow.DegreesToRadians(20);
				var FDRollTargetLPFFactorMaxFactor = Math.Min(Math.Abs(FDRollTargetRad) / FDRollTargetLPFFactorRollDeltaSmoothingRad, 1);
				var FDRollTargetLPFFactor = FDRollTargetLPFFactorMin + (FDRollTargetLPFFactorMax - FDRollTargetLPFFactorMin) * FDRollTargetLPFFactorMaxFactor;

				FDRollRad = LowPassFilter.Apply(
					FDRollRad,
					FDRollTargetRad,
					FDRollTargetLPFFactor
				);

				MainWindow.AircraftData.Computed.FlightDirectorRollRad = FDRollRad;

				// -------------------------------- Ailerons --------------------------------
				
				var aileronsTargetFactor = isFlying ? (rollTargetRadPredictedDelta > 0 ? 1 : 0) : 0.5;

				var aileronsLPFFactorMin = 0.0001;
				var aileronsLPFFactorMax = 0.01;
				var aileronsLPFFactorRollDeltaSmoothingRad = MainWindow.DegreesToRadians(30);
				var aileronsLPFFactorMaxFactorRollFactor = Math.Min(Math.Abs(rollTargetRadPredictedDelta) / aileronsLPFFactorRollDeltaSmoothingRad, 1);

				var aileronsLPFFactor =
					aileronsLPFFactorMin
					+ (aileronsLPFFactorMax - aileronsLPFFactorMin)
					* aileronsLPFFactorMaxFactorRollFactor;

				Ailerons = LowPassFilter.Apply(
					Ailerons,
					aileronsTargetFactor,
					aileronsLPFFactor
				);
			}
		}

		Timer.Change(TickInterval, Timeout.InfiniteTimeSpan);
	}
}
