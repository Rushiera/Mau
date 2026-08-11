using System;
using System.IO;

namespace Mau.Runtime
{
    /// <summary>
    /// Dog 持久化状态 DTO——序列化字段模型（IncludeFields）
    /// </summary>
    public sealed class DogState
    {
        /// <summary>
        /// Dog 名字
        /// </summary>
        public string Name = "";

        /// <summary>
        /// 工单大类
        /// </summary>
        public string OfficeType = "";

        /// <summary>
        /// 工单固定词汇
        /// </summary>
        public string OfficeName = "";

        /// <summary>
        /// OA 单 ID（审计用——恢复时重建新单）
        /// </summary>
        public long OfficeId;

        /// <summary>
        /// 超时帧数
        /// </summary>
        public long TimeoutTicks;

        /// <summary>
        /// 阶段名（Created/Waiting/PickingUp/Done）
        /// </summary>
        public string Phase = "Created";

        /// <summary>
        /// 是否超时终态（PickingUp 细分）
        /// </summary>
        public bool Timeouted;

        /// <summary>
        /// 请求载荷快照
        /// </summary>
        public OfficeData Payload;

        /// <summary>
        /// 回执快照
        /// </summary>
        public OfficeData Result;
    }

    /// <summary>
    /// 通用工单载体 Dog——OA 系统附属（IDog 默认实现）。
    /// 生命周期：Created → Post 建单 → Waiting → Closed/TimeOut → PickingUp → Collect → Done。
    /// 轮询由全局 Tick 驱动（FlowRunner 注册后自动调用 Tick()）。
    /// 持久化：DogState 落盘（工单描述/超时/载荷/回执）；恢复 = 重建 Dog + OA 重建单（载荷写回）。
    /// </summary>
    public class DogBase : IDog
    {
        /// <summary>
        /// OA 引用——构造注入
        /// </summary>
        private readonly IOA _oa;

        /// <summary>
        /// Flow 注册 ID
        /// </summary>
        private long _dogId;

        /// <summary>
        /// Dog 名字
        /// </summary>
        private string _dogName;

        /// <summary>
        /// 工单大类
        /// </summary>
        private string _officeType = "";

        /// <summary>
        /// 工单固定词汇
        /// </summary>
        private string _officeName = "";

        /// <summary>
        /// OA 单 ID
        /// </summary>
        private long _officeId;

        /// <summary>
        /// 超时帧数
        /// </summary>
        private long _timeoutTicks;

        /// <summary>
        /// 当前阶段
        /// </summary>
        private DogPhase _phase = DogPhase.Created;

        /// <summary>
        /// 超时终态标记（PickingUp 细分——Closed 还是 TimeOut）
        /// </summary>
        private bool _timeouted;
/// <summary>
/// PickingUp 停留帧数——超时自动闭合计数（未闭合兜底）
/// </summary>
private int _pickUpFrames; 
/// <summary>
/// PickingUp 自动闭合阈值——停留超过该帧数自动进入 Done（发单方崩溃/遗忘时防泄漏）
/// </summary>
 public  const  int  AutoCloseFrames  =  600 ;

        /// <summary>
        /// 请求载荷快照（持久化用）
        /// </summary>
        private OfficeData _payload;

        /// <summary>
        /// 回执快照
        /// </summary>
        private OfficeData _result;

        /// <summary>
        /// Flow 注册 ID
        /// </summary>
        public long DogId
        {
            get { return _dogId; }
        }

        /// <summary>
        /// Dog 名字
        /// </summary>
        public string DogName
        {
            get { return _dogName; }
        }

        /// <summary>
        /// 工单大类
        /// </summary>
        public string OfficeType
        {
            get { return _officeType; }
        }

        /// <summary>
        /// 工单固定词汇
        /// </summary>
        public string OfficeName
        {
            get { return _officeName; }
        }

        /// <summary>
        /// OA 单 ID
        /// </summary>
        public long OfficeId
        {
            get { return _officeId; }
        }

        /// <summary>
        /// 当前阶段
        /// </summary>
        public DogPhase Phase
        {
            get { return _phase; }
        }

        /// <summary>
        /// 构造通用 Dog
        /// </summary>
        /// <param name="oa">OA 引用</param>
        /// <param name="name">Dog 名字</param>
        public DogBase(IOA oa, string name)
        {
            if (oa == null)
            {
                throw new ArgumentNullException("oa");
            }
            _oa = oa;
            _dogName = name ?? "";
            _payload = OfficeData.Empty();
            _result = OfficeData.Empty();
        }

        /// <summary>
        /// 绑定注册 ID
        /// </summary>
        /// <param name="dogId">Flow 注册 ID</param>
        public void BindId(long dogId)
        {
            _dogId = dogId;
        }

        /// <summary>
        /// 建 OA 单并进入等待
        /// </summary>
        /// <param name="officeType">工单大类</param>
        /// <param name="officeName">工单固定词汇</param>
        /// <param name="timeoutTicks">超时帧数</param>
        /// <returns>true=建单成功</returns>
        public bool Post(string officeType, string officeName, long timeoutTicks)
        {
            if (officeType == null || officeName == null)
            {
                return false;
            }
            long officeId = _oa.Post(_dogId, officeType, officeName, timeoutTicks);
            if (officeId <= 0)
            {
                return false;
            }
            _officeId = officeId;
            _officeType = officeType;
            _officeName = officeName;
            _timeoutTicks = timeoutTicks;
            _phase = DogPhase.Waiting;
            return true;
        }

        /// <summary>
        /// 写请求载荷 int
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="value">int 值</param>
        /// <returns>true=写入成功</returns>
        public bool SetInt(string key, int value)
        {
            if (_phase != DogPhase.Waiting)
            {
                return false;
            }
            bool ok = _oa.SetInt(_officeId, _dogId, key, value);
            if (ok)
            {
                _payload.Ints[key] = value;
            }
            return ok;
        }

        /// <summary>
        /// 写请求载荷 str
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="value">str 值</param>
        /// <returns>true=写入成功</returns>
        public bool SetStr(string key, string value)
        {
            if (_phase != DogPhase.Waiting)
            {
                return false;
            }
            bool ok = _oa.SetStr(_officeId, _dogId, key, value);
            if (ok)
            {
                _payload.Strs[key] = value;
            }
            return ok;
        }

        /// <summary>
        /// IFlow.Tick——全局帧序驱动：轮询 OA 单状态
        /// </summary>
        public void Tick()
{
    if (_phase == DogPhase.Waiting)
    {
        OfficeState state = _oa.GetStatus(_officeId);
        if (state == OfficeState.Closed)
        {
            Office office = _oa.GetOffice(_officeId);
            _result = office.Result.Copy();
            _timeouted = false;
            _phase = DogPhase.PickingUp;
            _pickUpFrames = 0;
        }
        else if (state == OfficeState.TimeOut)
        {
            _result = OfficeData.Empty();
            _timeouted = true;
            _phase = DogPhase.PickingUp;
            _pickUpFrames = 0;
        }
    }
    else if (_phase == DogPhase.PickingUp)
    {
        _pickUpFrames = _pickUpFrames + 1;
        if (_pickUpFrames >= AutoCloseFrames)
        {
            // 超时未取回执——自动闭合（未闭合兜底：发单方崩溃/遗忘时防泄漏）
            _phase = DogPhase.Done;
            _officeId = 0;
        }
    }
}
        /// <summary>
        /// 单是否已正常关闭（Closed 且可取回执）
        /// </summary>
        /// <returns>true=Closed</returns>
        public bool IsClosed()
        {
            return _phase == DogPhase.PickingUp && !_timeouted;
        }

        /// <summary>
        /// 单是否已超时（TimeOut 且可取回执）
        /// </summary>
        /// <returns>true=TimeOut</returns>
        public bool IsTimeout()
        {
            return _phase == DogPhase.PickingUp && _timeouted;
        }

        /// <summary>
        /// 取回执双字典并进入 Done——仅 PickingUp 可调
        /// </summary>
        /// <param name="result">回执双字典（超时为空）</param>
        /// <returns>true=取回执成功</returns>
        public bool Collect(out OfficeData result)
        {
            if (_phase != DogPhase.PickingUp)
            {
                result = OfficeData.Empty();
                return false;
            }
            result = _result.Copy();
            _phase = DogPhase.Done;
            return true;
        }

        /// <summary>
        /// 读回执 int——仅 PickingUp 可调（不消费，finish 才回收）
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="value">int 值</param>
        /// <returns>true=Key 存在</returns>
        public bool GetResultInt(string key, out int value)
        {
            if (_phase != DogPhase.PickingUp)
            {
                value = 0;
                return false;
            }
            return _result.Ints.TryGetValue(key, out value);
        }

        /// <summary>
        /// 读回执 str——仅 PickingUp 可调（不消费，finish 才回收）
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="value">str 值</param>
        /// <returns>true=Key 存在</returns>
        public bool GetResultStr(string key, out string value)
        {
            if (_phase != DogPhase.PickingUp)
            {
                value = "";
                return false;
            }
            string? found;
            if (_result.Strs.TryGetValue(key, out found) && found != null)
            {
                value = found;
                return true;
            }
            value = "";
            return false;
        }

        /// <summary>
        /// 读请求载荷 str——请求方信息（session/call_id 等，任意阶段可读）
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="value">str 值</param>
        /// <returns>true=Key 存在</returns>
        public bool GetPayloadStr(string key, out string value)
        {
            string? found;
            if (_payload.Strs.TryGetValue(key, out found) && found != null)
            {
                value = found;
                return true;
            }
            value = "";
            return false;
        }

        /// <summary>
        /// 持久化落盘——原子写（临时文件 + 改名）
        /// </summary>
        /// <param name="path">目标路径</param>
        /// <returns>true=保存成功</returns>
        public bool Save(string path)
{
            if (path == null || path.Length == 0)
            {
                return false;
            }
            DogState state = new DogState();
            state.Name = _dogName;
            state.OfficeType = _officeType;
            state.OfficeName = _officeName;
            state.OfficeId = _officeId;
            state.TimeoutTicks = _timeoutTicks;
            state.Phase = _phase.ToString();
            state.Timeouted = _timeouted;
            state.Payload = _payload.Copy();
            state.Result = _result.Copy();
            try
            {
                System.Text.Json.JsonSerializerOptions options = new System.Text.Json.JsonSerializerOptions();
                options.IncludeFields = true;
                string json = System.Text.Json.JsonSerializer.Serialize(state, options);
                // 原子写——统一实现 ConfigStore.AtomicWrite（审查修复轮 2026-08-11 决策3）
                ConfigStore.AtomicWrite(path, json);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
        /// <summary>
        /// 从磁盘恢复——重建 Dog + OA 重建单（载荷写回）；注册由调用方负责
        /// </summary>
        /// <param name="path">存档路径</param>
        /// <param name="oa">OA 引用</param>
        /// <returns>Dog 实例，失败返回 null</returns>
        public IDog? TryLoad(string path, IOA oa)
        {
            if (path == null || path.Length == 0 || !File.Exists(path))
            {
                return null;
            }
            DogState? state;
            try
            {
                string json = File.ReadAllText(path);
                System.Text.Json.JsonSerializerOptions options = new System.Text.Json.JsonSerializerOptions();
                options.IncludeFields = true;
                state = System.Text.Json.JsonSerializer.Deserialize<DogState>(json, options);
            }
            catch (Exception)
            {
                return null;
            }
            if (state == null)
            {
                return null;
            }
            DogBase dog = new DogBase(oa, state.Name);
            dog._officeType = state.OfficeType;
            dog._officeName = state.OfficeName;
            dog._officeId = state.OfficeId;
            dog._timeoutTicks = state.TimeoutTicks;
            dog._timeouted = state.Timeouted;
            dog._payload = state.Payload.Copy();
            dog._result = state.Result.Copy();
            if (state.Phase == "Waiting")
            {
                // 恢复等待——OA 重建单 + 载荷写回（新单 ID）
                if (!dog.Post(state.OfficeType, state.OfficeName, state.TimeoutTicks))
                {
                    return null;
                }
                string[] intKeys = new string[state.Payload.Ints.Count];
                state.Payload.Ints.Keys.CopyTo(intKeys, 0);
                for (int i = 0; i < intKeys.Length; i = i + 1)
                {
                    dog.SetInt(intKeys[i], state.Payload.Ints[intKeys[i]]);
                }
                string[] strKeys = new string[state.Payload.Strs.Count];
                state.Payload.Strs.Keys.CopyTo(strKeys, 0);
                for (int i = 0; i < strKeys.Length; i = i + 1)
                {
                    dog.SetStr(strKeys[i], state.Payload.Strs[strKeys[i]]);
                }
            }
            else if (state.Phase == "PickingUp")
            {
                dog._phase = DogPhase.PickingUp;
            }
            else if (state.Phase == "Done")
            {
                dog._phase = DogPhase.Done;
            }
            else
            {
                dog._phase = DogPhase.Created;
            }
            return dog;
        }

        /// <summary>
        /// 清内部状态进入 Done（注册回收由调用方执行 FlowRunner.UnregisterFlow）
        /// </summary>
        public void Dispose()
        {
            _phase = DogPhase.Done;
            _officeId = 0;
        }
    }
}
