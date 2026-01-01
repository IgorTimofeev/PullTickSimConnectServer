using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PullTickSimConnectServer;

public class AicraftData {
	public double Throttle = 1;
	public double Ailerons = 1;
	public double Elevator = 1;
}

public class SimmData {
	public double AccelerationX = 0;

	public double RollRad = 0;
	public double PitchRad = 0;
	public double YawRad = 0;

	public double LatitudeRad = 0;
	public double LongitudeRad = 0;

	public double SpeedMPS = 0;

	public double PressurePA = 0;
	public double TemperatureC = 0;
}