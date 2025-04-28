using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PullTickSimConnectServer;

public class RemoteData {
	public double Throttle = 1;

	public double Ailerons = 0.5;
	public double Elevator = 0.5;
	public double Rudder = 0.5;

	public double Flaps = 0;
	public double Spoilers = 0;

	public double AltimeterPressurePa = 101325;

	public double AutopilotAirSpeedMs = 0;
	public bool AutopilotAutoThrottle = false;

	public double AutopilotHeadingRad = 0;
	public bool AutopilotHeadingHold = false;

	public double AutopilotAltitudeM = 0;
	public bool AutopilotLevelChange = false;

	public bool LandingGear = false;
	public bool StrobeLights = false;
}

public class AircraftDataComputed {
	public double Throttle = 0;

	public double GroundSpeedMs = 0;

	public double AltitudeM = 0;
	public double SlipAndSkidG = 0;

	public double WindDirectionDeg = 0;
	public double WindSpeedMs = 0;

	public double FlightPathPitchRad = 0;
	public double FlightPathYawRad = 0;

	public double FlightDirectorPitchRad = 0;
	public double FlightDirectorYawRad = 0;
}

public class AircraftData {
	public double LatitudeRad = 0;
	public double LongitudeRad = 0;

	public double PitchRad = 0;
	public double YawRad = 0;
	public double RollRad = 0;

	public double PressureHPa = 0;

	public double AirSpeedMs = 0;

	public AircraftDataComputed Computed = new();
}