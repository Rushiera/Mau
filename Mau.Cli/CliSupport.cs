using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Mau.Cli
{
    /// <summary>
    /// Mau.Cli 公共支撑——FindWorkspaceRoot / ComputeSha256 唯一实现（收敛原四份重复）
    /// </summary>
    public static class CliSupport
    {
        /// <summary>
        /// 查找 workspace 根——含 Mau.sln 的目录；当前目录向上优先，程序集位置兜底
        /// </summary>
        /// <returns>workspace 根或空</returns>
        public static string? FindWorkspaceRoot()
{
            // 统一探针——FindRepoRoot（审查修复轮 2026-08-11 决策2：5 处变体收敛）
            return FindRepoRoot(Directory.GetCurrentDirectory(), new string[] { "Mau.sln" }, AppContext.BaseDirectory);
        }/// <summary>
/// 统一仓库根探针——从起始目录向上查找含任一标记（文件或目录）的目录；fallbackDir 向上兜底
/// </summary>
/// <param name = "startDir">起始目录（优先查找链）</param>
/// <param name = "markers">标记集合——文件或目录名（如 Mau.sln / CatTemp / .git）</param>
/// <param name = "fallbackDir">兜底目录（如程序集位置）；空=不兜底</param>
/// <returns>仓库根或空</returns>
public static string? FindRepoRoot(string startDir, string[] markers, string? fallbackDir = null)
{
    string? Probe(string? from)
    {
        string? dir = from == null ? null : new DirectoryInfo(from).FullName;
        while (dir != null)
        {
            for (int i = 0; i < markers.Length; i = i + 1)
            {
                string candidate = Path.Combine(dir, markers[i]);
                if (File.Exists(candidate) || Directory.Exists(candidate))
                {
                    return dir;
                }
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        return null;
    }

    string? found = Probe(startDir);
    if (found != null)
    {
        return found;
    }

    if (fallbackDir != null && fallbackDir.Length > 0)
    {
        return Probe(fallbackDir);
    }

    return null;
}
        /// <summary>
        /// 计算字符串的 SHA256 哈希——UTF-8 字节转 64 位十六进制大写
        /// </summary>
        /// <param name="text">输入文本</param>
        /// <returns>64 位十六进制哈希（大写）</returns>
        public static string ComputeSha256(string text)
{
            // 统一实现下沉 Mau.Runtime.HashUtil（审查修复轮 2026-08-11——原 Cli 私有实现与 Translator.BrickEmbedder 重复）
            return Mau.Runtime.HashUtil.ComputeSha256(text);
        }/// <summary>
/// 详细输出开关——--verbose 全局生效（⑤ D18：两级分级——默认 + --verbose）
/// </summary>
public static bool Verbose = false; 
/// <summary>
/// 默认流——关键节点/结果/错误/摘要（⑤ D20 输出流工程条件：能进入流的条件）
/// </summary>
/// <param name = "message">输出行</param>
 public  static  void  Info ( string  message ) { Console . WriteLine ( message ) ;  } 
/// <summary>
/// 详细流——过程细节（--verbose 才输出；默认静默——源头降密度 D17）
/// </summary>
/// <param name = "message">输出行</param>
 public  static  void  Detail ( string  message ) { if  ( Verbose ) { Console . WriteLine ( message ) ;  } } 
/// <summary>
/// 解析 verbose 开关——命令参数含 --verbose 时启用（命令入口统一调用；全局静态，一次设置全命令生效）
/// </summary>
/// <param name = "args">命令参数</param>
 public  static  void  ParseVerbose ( string [ ]  args ) { for  ( int  i  =  0 ;  i < args . Length ;  i  =  i + 1 ) { if  ( args [ i ] == "--verbose" ) { Verbose  =  true ;  return ;  } } } 
/// <summary>
/// 尾部保留截断——从尾部保留最后 N 行（⑤ D19：错误/结论在尾部；正常流程不该触发）
/// </summary>
/// <param name = "text">原始文本</param>
/// <param name = "maxLines">保留行数</param>
/// <returns>截断后文本——空输入返回（无输出）</returns>
 public  static  string  TailLines ( string  text ,  int  maxLines ) { if  ( text == null  || text . Trim ( ) . Length == 0 ) { return  "（无输出）" ;  } string [ ]  lines  =  text . Replace ( "\r\n" ,  "\n" ) . Split ( '\n' ) ;  int  start  =  lines . Length > maxLines ? lines . Length - maxLines :  0 ;  StringBuilder  sb  =  new  StringBuilder ( ) ;  for  ( int  i  =  start ;  i < lines . Length ;  i  =  i + 1 ) { sb . AppendLine ( lines [ i ] ) ;  } return  sb . ToString ( ) ;  }
/// <summary>
/// 探测文件是否被进程占用——以写方式独占打开（成功=未锁；IOException=被锁）。
/// ⑦ 统一遇锁即失败协议（D31）：自身指令遇锁冲突立即失败带诊断，不等待。
/// </summary>
/// <param name = "paths">待探测路径</param>
/// <returns>第一个被锁路径；全部未锁返回空串</returns>
public static string FindLockedFile(string[] paths)
{
    for (int i = 0; i < paths.Length; i = i + 1)
    {
        if (!File.Exists(paths[i]))
        {
            continue;
        }

        try
        {
            using (FileStream fs = new FileStream(paths[i], FileMode.Open, FileAccess.Write, FileShare.None))
            {
            // 探测成功——未锁
            }
        }
        catch (IOException)
        {
            return paths[i];
        }
        catch (UnauthorizedAccessException)
        {
            return paths[i];
        }
    }

    return "";
}    /// <summary>
/// JSON 字符串转义——统一实现（原 CommandBricks/CommandMauProj/CommandRun/HttpTransport 四份重复，审查修复轮收拢）
/// </summary>
/// <param name = "s">原始字符串，可为 null</param>
/// <returns>转义后字符串</returns>
public static string JsonEscape(string? s)
{
            // 统一实现下沉 Mau.Runtime.TextUtil（审查修复轮 2026-08-11——原四份独立实现收拢）
            return Mau.Runtime.TextUtil.JsonEscape(s);
        }/// <summary>
/// 构建最小语料——信号触发 + 单变迁 + 双后置（积木冒烟/积木谱/smoke 共用模板；审查修复轮 2026-08-11 决策6）
/// </summary>
/// <param name = "brickName">动作积木名</param>
/// <param name = "paramLines">参数声明（已拼接，可为空串）</param>
/// <param name = "transitionName">变迁名（默认 T_Run）</param>
/// <returns>.mau 源码</returns>
public static string BuildMinimalCorpus(string brickName, string paramLines, string transitionName = "T_Run")
{
    return "Mau 0.1\n基座: Mau.Runtime/v0.1\n\n命题:\n  P_Go 信号\n  P_Done 事实\n  P_Failed 事实\n\n变迁 " + transitionName + ":\n  前置: P_Go\n  动作: " + brickName + "\n  参数: " + paramLines + "\n  时限: 60帧\n  后置: P_Done / P_Failed\n";
}}
}
