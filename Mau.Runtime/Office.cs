namespace Mau.Runtime
{
    /// <summary>
    /// OA 工单。挂单方投递到 OA 的不可变描述 + OA 维护的可变状态。
    /// 参数和结果均为数组——快生命周期不做复杂容器（不用 Dictionary）。
    /// </summary>
    public struct Office
    {
        /// <summary>
        /// OA 内自增唯一 ID
        /// </summary>
        public long OfficeId;

        /// <summary>
        /// 工单大类——"OI_IO" / "OI_LLM" / "OI_TCP" ...
        /// </summary>
        public string OfficeType;

        /// <summary>
        /// 固定词汇——执行方据此判断自己能不能干
        /// </summary>
        public string OfficeName;

        /// <summary>
        /// 所有者（挂单方）的 LongId
        /// </summary>
        public long DogId;

        /// <summary>
        /// 文本参数数组
        /// </summary>
        public string[] Texts;

        /// <summary>
        /// 文件路径参数数组
        /// </summary>
        public string[] Paths;

        /// <summary>
        /// 当前状态
        /// </summary>
        public OfficeState Status;

        /// <summary>
        /// 认领者 LongId（0=未认领）
        /// </summary>
        public long ClaimByCatId;

        /// <summary>
        /// 上架帧号
        /// </summary>
        public long PostFrame;

        /// <summary>
        /// 认领帧号
        /// </summary>
        public long ClaimFrame;

        /// <summary>
        /// 挂单方设定的超时帧数
        /// </summary>
        public long TimeoutFrames;

        /// <summary>
        /// 回执文本数组（Closed 后有效）
        /// </summary>
        public string[] ResultTexts;

        /// <summary>
        /// 回执路径数组（Closed 后有效）
        /// </summary>
        public string[] ResultPaths;
    }
}
