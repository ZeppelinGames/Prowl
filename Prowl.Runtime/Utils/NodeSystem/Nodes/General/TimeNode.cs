﻿namespace Prowl.Runtime.NodeSystem
{
    [Node("General")]
    public class TimeNode : Node
    {
        public override bool ShowTitle => false;
        public override string Title => "Time";
        public override float Width => 50;

        [Output, SerializeIgnore] public double Time;

        public override object GetValue(NodePort port) => Runtime.Time.time;
    }
}