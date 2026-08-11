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
            DirectoryInfo? dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Mau.sln")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            DirectoryInfo? exeDir = new DirectoryInfo(AppContext.BaseDirectory);
            while (exeDir != null)
            {
                if (File.Exists(Path.Combine(exeDir.FullName, "Mau.sln")))
                {
                    return exeDir.FullName;
                }
                exeDir = exeDir.Parent;
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
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            byte[] hash = SHA256.HashData(bytes);
            StringBuilder hex = new StringBuilder();
            for (int i = 0; i < hash.Length; i++)
            {
                hex.Append(hash[i].ToString("X2"));
            }
            return hex.ToString();
        }
/// <summary>
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
}    }
}
