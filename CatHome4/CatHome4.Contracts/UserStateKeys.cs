namespace CatHome4.Contracts
{
    /// <summary>
    /// 用户态配置键——当前用户 / QQ 非用户指令开关 / 群内身份清单（跨域单一真相源）。
    /// 消费面：Admin 域（/api/v1/user-state 读写 + 注入段）与 QQ 域（消息头用户名与身份标记）——
    /// 两域分属不同程序集，键名常量收敛在此，避免字面量双写漂移（规格：Project/CH4/design-ch4-user-state.md）。
    /// </summary>
    public static class UserStateKeys
    {
        /// <summary>配置键——当前用户（值 = 显示名：莎 / Rushiera）</summary>
        public const string Current = "user.current";

        /// <summary>配置键——接受非用户的指令（bool 文本：true / false）</summary>
        public const string QqAcceptNonUser = "user.qq_accept_nonuser";

        /// <summary>配置键——当前用户群内身份清单（昵称 / member_openid / union_openid，逗号分隔）</summary>
        public const string QqIds = "user.qq_ids";

        /// <summary>缺省用户显示名——配置缺失 / 非法时回落（与 Admin 域用户档案表首位同名）</summary>
        public const string DefaultUserName = "莎";
    }
}
