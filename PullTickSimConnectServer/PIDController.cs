using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace PullTickSimConnectServer;

public class PIDController(double p, double i, double d, double min, double max) {
	public double P { get; set; } = p;
	public double I { get; set; } = i;
	public double D { get; set; } = d;
	public double Min { get; set; } = min;
	public double Max { get; set; } = max;

	double Integral = 0;
	double PreviousError = 0;

	public double Update(double input, double setpoint, double deltaTimeSeconds) {
		var error = setpoint - input;

		Integral = Math.Clamp(Integral + error * deltaTimeSeconds, Min, Max);

		var derivative = (error - PreviousError) / deltaTimeSeconds;
		PreviousError = error;

		return Math.Clamp(error * P + Integral * I + derivative * D, Min, Max);
	}
}
