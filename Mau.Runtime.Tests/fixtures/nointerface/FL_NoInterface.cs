namespace Mau.TestFixtures
{
    /// <summary>
    /// 测试用无效生成物——不实现 IObservableFlow，验证 FlowHandle.Load 报错
    /// </summary>
    public sealed class FL_NoInterface
    {
        private long _frame;

        public FL_NoInterface()
        {
            _frame = 0;
        }

        public void Tick()
        {
            _frame = _frame + 1;
        }

        public long GetFrame()
        {
            return _frame;
        }
    }
}
