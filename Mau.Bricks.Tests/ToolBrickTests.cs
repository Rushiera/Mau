// ═══════════════════════════════════════════════
// 测试: Mau.Bricks.Standard——ToolBrick（工具执行机制积木）
// 引用: Mau.Bricks.Tests → Mau.Bricks.Standard + Mau.Contracts
// 原理: 执行器注入 Configure → 分发验证；未注入时拒绝
// 常用: tool.exec 积木正确性回归——oa_flow/tool_dispatch 语料前置
// ═══════════════════════════════════════════════
using System;
using Mau.Bricks;
using Mau.Runtime;
using Xunit;

namespace Mau.Bricks.Tests
{
    /// <summary>
    /// 工具执行积木测试——执行器注入分发 + 未注入防护 + dispatch/collect 统合
    /// </summary>
    [Collection("FileBrickShared")]
    public sealed class ToolBrickTests
    {
        /// <summary>
        /// 未注入执行器时返回 false——积木不可静默空转
        /// </summary>
        [Fact]
        public void ToolBrick_Unconfigured_ReturnsFalse()
        {
            string[] result;
            Assert.False(ToolBrick.Exec("file.read", new string[] { "x" }, out result));
        }

        /// <summary>
        /// 注入后按工具名分发——参数完整传递
        /// </summary>
        [Fact]
        public void ToolBrick_Exec_DispatchToExecutor()
        {
            ToolBrick.Configure(delegate (string name, string[] args)
            {
                string joined = "";
                for (int i = 0; i < args.Length; i++)
                {
                    if (i > 0)
                    {
                        joined = joined + ",";
                    }
                    joined = joined + args[i];
                }
                return new string[] { name + ":" + joined };
            });
            try
            {
                string[] result;
                Assert.True(ToolBrick.Exec("file.read", new string[] { "a.txt", "b.txt" }, out result));
                Assert.Equal("file.read:a.txt,b.txt", result[0]);
            }
            finally
            {
                ToolBrick.Configure(null!);
            }
        }

        /// <summary>
        /// dispatch——解析 tool_calls JSON → 发多单（TOOL 大类 + 工具名 officeName + call_id/args.* 展平载荷）
        /// </summary>
        [Fact]
        public void ToolBrick_Dispatch_PostsToolOffices()
        {
            ThreadGuard guard = new ThreadGuard();
            OA oa = new OA(guard);
            ToolBrick.ConfigureOA(oa);
            try
            {
                string calls = "[{\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"file.read\",\"arguments\":{\"path\":\"a.txt\"}}},{\"id\":\"call_2\",\"type\":\"function\",\"function\":{\"name\":\"log.write\",\"arguments\":{\"level\":\"INFO\"}}}]";
                long[] officeIds;
                Assert.True(ToolBrick.Dispatch(calls, 1, 300, out officeIds));
                Assert.Equal(2, officeIds.Length);

                // 两单均为 TOOL 大类 + 工具名 + 展平载荷 Key
                Office first = oa.GetOffice(officeIds[0]);
                Assert.Equal("TOOL", first.OfficeType);
                Assert.Equal("file.read", first.OfficeName);
                Assert.Equal("call_1", first.Data.Strs["call_id"]);
                Assert.Equal("a.txt", first.Data.Strs["args.path"]);

                Office second = oa.GetOffice(officeIds[1]);
                Assert.Equal("log.write", second.OfficeName);
                Assert.Equal("call_2", second.Data.Strs["call_id"]);
                Assert.Equal("INFO", second.Data.Strs["args.level"]);
            }
            finally
            {
                ToolBrick.ConfigureOA(null!);
            }
        }

        /// <summary>
        /// collect——按序读回执拼装统合 JSON
        /// </summary>
        [Fact]
        public void ToolBrick_Collect_MergesResultsInOrder()
        {
            ThreadGuard guard = new ThreadGuard();
            OA oa = new OA(guard);
            ToolBrick.ConfigureOA(oa);
            try
            {
                string calls = "[{\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"file.read\",\"arguments\":\"\"}},{\"id\":\"call_2\",\"type\":\"function\",\"function\":{\"name\":\"log.write\",\"arguments\":\"\"}}]";
                long[] officeIds;
                Assert.True(ToolBrick.Dispatch(calls, 1, 300, out officeIds));
                Assert.Equal(2, officeIds.Length);

                // 模拟工具执行方——认领 → 写回执 → 完成
                oa.ClaimBatch(2, officeIds);
                for (int i = 0; i < officeIds.Length; i = i + 1)
                {
                    OfficeData result = OfficeData.Empty();
                    result.Strs["content"] = "结果" + i.ToString();
                    oa.Complete(officeIds[i], 2, result);
                }

                string merged;
                Assert.True(ToolBrick.Collect(officeIds, out merged));
                // Utf8JsonWriter 默认转义非 ASCII——解析 JSON 验证结构
                using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(merged))
                {
                    System.Text.Json.JsonElement root = doc.RootElement;
                    Assert.Equal(System.Text.Json.JsonValueKind.Array, root.ValueKind);
                    Assert.Equal(2, root.GetArrayLength());
                    Assert.Equal("call_1", root[0].GetProperty("call_id").GetString());
                    Assert.Equal("结果0", root[0].GetProperty("content").GetString());
                    Assert.Equal("call_2", root[1].GetProperty("call_id").GetString());
                    Assert.Equal("结果1", root[1].GetProperty("content").GetString());
                }
            }
            finally
            {
                ToolBrick.ConfigureOA(null!);
            }
        }

        /// <summary>
        /// is_name——officeName 匹配判断（语料分支用）
        /// </summary>
        [Fact]
        public void ToolBrick_IsName_MatchesOfficeName()
        {
            ThreadGuard guard = new ThreadGuard();
            OA oa = new OA(guard);
            ToolBrick.ConfigureOA(oa);
            try
            {
                long[] officeIds;
                string calls = "[{\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"file.read\",\"arguments\":\"\"}}]";
                Assert.True(ToolBrick.Dispatch(calls, 1, 300, out officeIds));

                bool matched;
                // 匹配 → 返回 true（判断结果）
                Assert.True(ToolBrick.IsName(officeIds[0], "file.read", out matched));
                Assert.True(matched);
                // 不匹配 → 返回 false（判断结果）
                Assert.False(ToolBrick.IsName(officeIds[0], "log.write", out matched));
                Assert.False(matched);
            }
            finally
            {
                ToolBrick.ConfigureOA(null!);
            }
        }

        /// <summary>
        /// dispatch_one——按索引逐单发；越界返回 false；arguments 对象展平为 args.* Key
        /// </summary>
        [Fact]
        public void ToolBrick_DispatchOne_ByIndex()
        {
            ThreadGuard guard = new ThreadGuard();
            OA oa = new OA(guard);
            ToolBrick.ConfigureOA(oa);
            try
            {
                string calls = "[{\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"file.read\",\"arguments\":{\"path\":\"a.txt\"}}},{\"id\":\"call_2\",\"type\":\"function\",\"function\":{\"name\":\"log.write\",\"arguments\":{\"level\":\"INFO\"}}}]";
                long officeId0;
                string name0;
                Assert.True(ToolBrick.DispatchOne(calls, 0, 1, 300, out officeId0, out name0));
                Assert.Equal("file.read", name0);
                Assert.Equal("file.read", oa.GetOffice(officeId0).OfficeName);
                Assert.Equal("a.txt", oa.GetOffice(officeId0).Data.Strs["args.path"]);

                long officeId1;
                string name1;
                Assert.True(ToolBrick.DispatchOne(calls, 1, 1, 300, out officeId1, out name1));
                Assert.Equal("log.write", name1);

                long officeId2;
                string name2;
                Assert.False(ToolBrick.DispatchOne(calls, 2, 1, 300, out officeId2, out name2));
                Assert.Equal(0, officeId2);
            }
            finally
            {
                ToolBrick.ConfigureOA(null!);
            }
        }

        /// <summary>
        /// run_file_read——适配器全流程：读单参数 → FileBrick.Read → 回执 → Complete
        /// </summary>
        [Fact]
        public void ToolBrick_RunFileRead_ReadsAndCompletes()
        {
            ThreadGuard guard = new ThreadGuard();
            OA oa = new OA(guard);
            ToolBrick.ConfigureOA(oa);
            // FileBrick 静态根可能被 FileBrickTests 改过——本测试显式配置（集合串行内安全）
            string workRoot = Environment.CurrentDirectory;
            FileBrick.ConfigureRoots(new string[] { workRoot },
                System.IO.Path.Combine(workRoot, "Recycle"));
            // 测试文件放工作目录内
            string tempFile = System.IO.Path.Combine(workRoot,
                "ch4tool_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".txt");
            System.IO.File.WriteAllText(tempFile, "工具读取的内容");
            try
            {
                // 展平协议：arguments 为对象——calls 直接嵌套（无需字符串转义链）
                string calls = "[{\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"file.read\",\"arguments\":{\"path\":\"" + tempFile.Replace("\\", "\\\\") + "\"}}}]";
                long officeId;
                string name;
                Assert.True(ToolBrick.DispatchOne(calls, 0, 1, 300, out officeId, out name));

                // 执行方认领 → 适配器执行
                oa.ClaimBatch(2, new long[] { officeId });
                string rawPath;
                Assert.True(oa.GetStr(officeId, "args.path", out rawPath), "args.path Key 缺失");
                // 诊断：FileBrick.Read 直调验证（动态路径）
                string fbContent;
                bool fbOk = FileBrick.Read(tempFile, out fbContent);
                Assert.True(fbOk, "FileBrick.Read 直调失败——" + fbContent);
                string result;
                string callId;
                string session;
                Assert.True(ToolBrick.RunFileRead(officeId, 2, out result, out callId, out session),
                    "RunFileRead 失败——args.path=" + rawPath);
                Assert.Equal("工具读取的内容", result);
                Assert.Equal(OfficeState.Closed, oa.GetStatus(officeId));
                Assert.Equal("工具读取的内容", oa.GetOffice(officeId).Result.Strs["content"]);
            }
            finally
            {
                ToolBrick.ConfigureOA(null!);
                if (System.IO.File.Exists(tempFile))
                {
                    System.IO.File.Delete(tempFile);
                }
            }
        }

        /// <summary>
        /// run_file_write——适配器写文件 + 回执 + Complete
        /// </summary>
        [Fact]
        public void ToolBrick_RunFileWrite_WritesAndCompletes()
        {
            ThreadGuard guard = new ThreadGuard();
            OA oa = new OA(guard);
            ToolBrick.ConfigureOA(oa);
            string workRoot = Environment.CurrentDirectory;
            FileBrick.ConfigureRoots(new string[] { workRoot },
                System.IO.Path.Combine(workRoot, "Recycle"));
            string targetFile = System.IO.Path.Combine(workRoot,
                "ch4toolw_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".txt");
            try
            {
                string calls = "[{\"id\":\"call_w\",\"type\":\"function\",\"function\":{\"name\":\"file.write\",\"arguments\":{\"path\":\"" + targetFile.Replace("\\", "\\\\") + "\",\"content\":\"写入的正文\"}}}]";
                long officeId;
                string name;
                Assert.True(ToolBrick.DispatchOne(calls, 0, 1, 300, out officeId, out name));
                Assert.Equal("file.write", name);
                Assert.Equal("写入的正文", oa.GetOffice(officeId).Data.Strs["args.content"]);

                oa.ClaimBatch(2, new long[] { officeId });
                string result;
                string callId;
                string session;
                Assert.True(ToolBrick.RunFileWrite(officeId, 2, out result, out callId, out session));
                Assert.Equal(OfficeState.Closed, oa.GetStatus(officeId));
                Assert.True(System.IO.File.Exists(targetFile));
                Assert.Equal("写入的正文", System.IO.File.ReadAllText(targetFile));
            }
            finally
            {
                ToolBrick.ConfigureOA(null!);
                if (System.IO.File.Exists(targetFile))
                {
                    System.IO.File.Delete(targetFile);
                }
            }
        }

        /// <summary>
        /// dispatch_next——游标式逐单发（sessionKey 游标）→ 越界 false + 游标重置
        /// </summary>
        [Fact]
        public void ToolBrick_DispatchNext_CursorAdvances()
        {
            ThreadGuard guard = new ThreadGuard();
            OA oa = new OA(guard);
            ToolBrick.ConfigureOA(oa);
            try
            {
                string calls = "[{\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"file.read\",\"arguments\":{\"path\":\"a.txt\"}}},{\"id\":\"call_2\",\"type\":\"function\",\"function\":{\"name\":\"file.write\",\"arguments\":{\"path\":\"b.txt\"}}}]";
                long office0;
                string name0;
                Assert.True(ToolBrick.DispatchNext(calls, "sess-1", 1, 300, out office0, out name0));
                Assert.Equal("file.read", name0);
                Assert.Equal("sess-1", oa.GetOffice(office0).Data.Strs["session"]);

                long office1;
                string name1;
                Assert.True(ToolBrick.DispatchNext(calls, "sess-1", 1, 300, out office1, out name1));
                Assert.Equal("file.write", name1);

                // 越界——本轮完毕（游标重置，下一轮从 0 重新开始）
                long office2;
                string name2;
                Assert.False(ToolBrick.DispatchNext(calls, "sess-1", 1, 300, out office2, out name2));
                Assert.Equal(0, office2);
                Assert.True(ToolBrick.DispatchNext(calls, "sess-1", 1, 300, out office2, out name2));
                Assert.Equal("file.read", name2);
            }
            finally
            {
                ToolBrick.ConfigureOA(null!);
            }
        }

        /// <summary>
        /// claim_next——游标式认领（无单返回 false 继续轮询）
        /// </summary>
        [Fact]
        public void ToolBrick_ClaimNext_ClaimsOneTool()
        {
            ThreadGuard guard = new ThreadGuard();
            OA oa = new OA(guard);
            ToolBrick.ConfigureOA(oa);
            try
            {
                string calls = "[{\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"file.read\",\"arguments\":{\"path\":\"a.txt\"}}}]";
                long officeId;
                string name;
                Assert.True(ToolBrick.DispatchNext(calls, "sess-2", 1, 300, out officeId, out name));

                // 有单——认领成功（第一次调用即认领 dispatch_next 发的单）
                long c0;
                string n0;
                Assert.True(ToolBrick.ClaimNext(2, out c0, out n0));
                Assert.Equal(officeId, c0);
                Assert.Equal("file.read", n0);
                Assert.Equal(OfficeState.Work, oa.GetStatus(officeId));
                // 认领后无 Open 单——false（继续轮询）
                Assert.False(ToolBrick.ClaimNext(2, out c0, out n0));
            }
            finally
            {
                ToolBrick.ConfigureOA(null!);
            }
        }

        /// <summary>
        /// collect_one——单值收集回执（call_id + content）
        /// </summary>
        [Fact]
        public void ToolBrick_CollectOne_ReadsReceipt()
        {
            ThreadGuard guard = new ThreadGuard();
            OA oa = new OA(guard);
            ToolBrick.ConfigureOA(oa);
            try
            {
                string calls = "[{\"id\":\"call_x\",\"type\":\"function\",\"function\":{\"name\":\"file.read\",\"arguments\":{\"path\":\"a.txt\"}}}]";
                long officeId;
                string name;
                Assert.True(ToolBrick.DispatchNext(calls, "sess-3", 1, 300, out officeId, out name));
                oa.ClaimBatch(2, new long[] { officeId });
                OfficeData result = OfficeData.Empty();
                result.Strs["content"] = "结果文本";
                oa.Complete(officeId, 2, result);

                string callId;
                string content;
                Assert.True(ToolBrick.CollectOne(officeId, out callId, out content));
                Assert.Equal("call_x", callId);
                Assert.Equal("结果文本", content);
            }
            finally
            {
                ToolBrick.ConfigureOA(null!);
            }
        }

        /// <summary>
        /// run_file_read 附带输出——callId/session 从工具单读取
        /// </summary>
        [Fact]
        public void ToolBrick_RunFileRead_OutputsCallIdAndSession()
        {
            ThreadGuard guard = new ThreadGuard();
            OA oa = new OA(guard);
            ToolBrick.ConfigureOA(oa);
            string workRoot = Environment.CurrentDirectory;
            FileBrick.ConfigureRoots(new string[] { workRoot },
                System.IO.Path.Combine(workRoot, "Recycle"));
            string tempFile = System.IO.Path.Combine(workRoot,
                "ch4toolr_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".txt");
            System.IO.File.WriteAllText(tempFile, "附带输出测试");
            try
            {
                string calls = "[{\"id\":\"call_y\",\"type\":\"function\",\"function\":{\"name\":\"file.read\",\"arguments\":{\"path\":\"" + tempFile.Replace("\\", "\\\\") + "\"}}}]";
                long officeId;
                string name;
                Assert.True(ToolBrick.DispatchNext(calls, "sess-4", 1, 300, out officeId, out name));
                oa.ClaimBatch(2, new long[] { officeId });

                string result;
                string callId;
                string session;
                Assert.True(ToolBrick.RunFileRead(officeId, 2, out result, out callId, out session));
                Assert.Equal("附带输出测试", result);
                Assert.Equal("call_y", callId);
                Assert.Equal("sess-4", session);
            }
            finally
            {
                ToolBrick.ConfigureOA(null!);
                if (System.IO.File.Exists(tempFile))
                {
                    System.IO.File.Delete(tempFile);
                }
            }
        }
    }
}

