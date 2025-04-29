using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace PullTickSimConnectServer;

public class PIDController {
	public PIDController(double p, double i, double d, double min, double max) {
		P = p;
		I = i;
		D = d;
		Min = min;
		Max = max;
	}

	public double P { get; set; }
	public double I { get; set; }
	public double D { get; set; }
	public double Min { get; set; }
	public double Max { get; set; }

	double Integral = 0;
	double PreviousError = 0;

	public double Update(double input, double setpoint, double deltaTimeSeconds) {
		var error = setpoint - input;

		Integral = Math.Clamp(Integral + error * deltaTimeSeconds * I, Min, Max);

		var derivative = (error - PreviousError) / deltaTimeSeconds * D;
		PreviousError = error;

		return Math.Clamp(error * P + Integral + derivative, Min, Max);
	}
}
