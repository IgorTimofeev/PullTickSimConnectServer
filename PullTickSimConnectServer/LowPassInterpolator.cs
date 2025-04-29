namespace PullTickSimConnectServer;

public class LowPassInterpolator {
	public LowPassInterpolator(double factor) {
		Factor = factor;
	}

	public void Tick() {
		Value = Value * (1 - Factor) + TargetValue * Factor;
	}

	public double Factor { get; set; } = 0;
	public double TargetValue { get; set; } = 0;
	public double Value { get; set; } = 0;
}
