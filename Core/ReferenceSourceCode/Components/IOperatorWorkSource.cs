namespace Game.Components {
    // A device that can keep its operator (OperatableDeviceComp.CanKeepOperator) tells whether
    // there is work right now. A kept operator sits there between jobs, and used to train the
    // device's skill all the while: Keep Operator was free training.
    public interface IOperatorWorkSource : IComponent {
        bool IsOperatorWorking { get; }
    }
}
