using Mau.Runtime;

namespace Mau.TestFixtures
{
    /// <summary>
    /// 测试用正常生成物——实现 IObservableFlow，Tick 中自增帧号
    /// </summary>
    public sealed class FL_ValidFlow : IObservableFlow
    {
        private long _frame;
        private FlowLog _logs;
        private bool _done;

        public FL_ValidFlow()
        {
            _logs = new FlowLog();
            _done = false;
        }

        public void Tick()
        {
            _frame = _frame + 1;
            if (_frame >= 5)
            {
                _done = true;
            }
            _logs.Add(new MauDebug(_frame, "T_Test", "Fired", "tick " + _frame.ToString()));
        }

        public RuntimeStatus GetStatus()
        {
            PropSnapshot[] props = new PropSnapshot[]
            {
                new PropSnapshot("P_Done", "Fact", _done)
            };
            TransSnapshot[] trans = new TransSnapshot[]
            {
                new TransSnapshot("T_Test", "Idle", 0, 0)
            };
            ResSnapshot[] res = new ResSnapshot[0];
            return new RuntimeStatus(_frame, props, trans, res);
        }

        public MauDebug[] GetLogs()
        {
            return _logs.GetAll();
        }
    }
}
