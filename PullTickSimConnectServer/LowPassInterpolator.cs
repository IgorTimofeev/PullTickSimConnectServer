namespace PullTickSimConnectServer;

public class LowPassInterpolator(double factor) {
	public void Tick() {
		Value = Value * (1 - factor) + TargetValue * factor;
	}

	public double TargetValue { get; set; } = 0;
	public double Value { get; set; } = 0;
}
