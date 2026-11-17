///------------------------------------
/// Author：Novorizon
/// Mail：novorizon@hotmail.com
/// Date：2022-10-11
/// Description：Task扩展
///------------------------------------
using System;
using System.Threading.Tasks;


namespace Game.Framework
{
    /// <summary>任务对应的扩展方法。为相关类型提供扩展方法。</summary>
    public static class TaskExtensions
    {
        /// <summary>观察无需等待的任务，防止故障无人处理。</summary>
        public static async void Forget(this Task task)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Logger.Exception(ex);
            }
        }

        /// <summary>观察无需等待的任务，防止故障无人处理。</summary>
        public static async void Forget<T>(this Task<T> task)
        {
            try
            {
                _ = await task;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Logger.Exception(ex);
            }
        }
    }


}
