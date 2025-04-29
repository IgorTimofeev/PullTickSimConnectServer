namespace PullTickSimConnectServer;

public class LowPassFilter {
	public static double Apply(double value, double targetValue, double factor) {
		return value * (1 - factor) + targetValue * factor;
	}
}
