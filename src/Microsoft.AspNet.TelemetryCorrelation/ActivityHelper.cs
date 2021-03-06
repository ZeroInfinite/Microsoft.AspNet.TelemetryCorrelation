﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Diagnostics;
using System.Web;

namespace Microsoft.AspNet.TelemetryCorrelation
{
    /// <summary>
    /// Activity helper class
    /// </summary>
    internal static class ActivityHelper
    {
        /// <summary>
        /// Listener name.
        /// </summary>
        public const string AspNetListenerName = "Microsoft.AspNet.TelemetryCorrelation";

        /// <summary>
        /// Activity name for http request.
        /// </summary>
        public const string AspNetActivityName = "Microsoft.AspNet.HttpReqIn";

        /// <summary>
        /// Event name for the activity start event.
        /// </summary>
        public const string AspNetActivityStartName = "Microsoft.AspNet.HttpReqIn.Start";

        /// <summary>
        /// Event name for the activity stop event.
        /// </summary>
        public const string AspNetActivityLostStopName = "Microsoft.AspNet.HttpReqIn.ActivityLost.Stop";

        /// <summary>
        /// Key to store the activity in HttpContext.
        /// </summary>
        public const string ActivityKey = "__AspnetActivity__";

        private static readonly DiagnosticListener AspNetListener = new DiagnosticListener(AspNetListenerName);

        /// <summary>
        /// It's possible that a request is executed in both native threads and managed threads,
        /// in such case Activity.Current will be lost during native thread and managed thread switch.
        /// This method is intended to restore the current activity in order to correlate the child
        /// activities with the root activity of the request.
        /// </summary>
        /// <param name="root">Root activity id for the current request.</param>
        /// <returns>If it returns an activity, it will be silently stopped with the parent activity</returns>
        public static Activity RestoreCurrentActivity(Activity root)
        {
            Debug.Assert(root != null);

            // workaround to restore the root activity, because we don't
            // have a way to change the Activity.Current
            var childActivity = new Activity(root.OperationName);
            childActivity.SetParentId(root.Id);
            childActivity.SetStartTime(root.StartTimeUtc);
            foreach (var item in root.Baggage)
            {
                childActivity.AddBaggage(item.Key, item.Value);
            }

            childActivity.Start();

            AspNetTelemetryCorrelationEventSource.Log.ActivityStarted(childActivity.Id);
            return childActivity;
        }

        public static bool StopAspNetActivity(Activity activity, HttpContext context)
        {
            if (activity != null && Activity.Current != null)
            {
                // silently stop all child activities before activity
                while (Activity.Current != activity && Activity.Current != null)
                {
                    Activity.Current.Stop();
                }

                // if activity is in the stack, stop it with Stop event
                if (Activity.Current != null)
                {
                    AspNetListener.StopActivity(Activity.Current, new { });
                    RemoveCurrentActivity(context);
                    AspNetTelemetryCorrelationEventSource.Log.ActivityStopped(activity.Id);
                    return true;
                }
            }

            return false;
        }

        public static void StopLostActivity(Activity activity, HttpContext context)
        {
            if (activity != null)
            {
                AspNetListener.Write(AspNetActivityLostStopName, new { activity });
                RemoveCurrentActivity(context);
                AspNetTelemetryCorrelationEventSource.Log.ActivityStopped(activity.Id, true);
            }
        }

        public static Activity CreateRootActivity(HttpContext context)
        {
            if (AspNetListener.IsEnabled() && AspNetListener.IsEnabled(AspNetActivityName))
            {
                var rootActivity = new Activity(ActivityHelper.AspNetActivityName);

                rootActivity.Extract(context.Request.Unvalidated.Headers);
                if (StartAspNetActivity(rootActivity))
                {
                    SaveCurrentActivity(context, rootActivity);
                    AspNetTelemetryCorrelationEventSource.Log.ActivityStarted(rootActivity.Id);
                    return rootActivity;
                }
            }

            return null;
        }

        private static bool StartAspNetActivity(Activity activity)
        {
            if (AspNetListener.IsEnabled(AspNetActivityName, activity, new { }))
            {
                if (AspNetListener.IsEnabled(AspNetActivityStartName))
                {
                    AspNetListener.StartActivity(activity, new { });
                }
                else
                {
                    activity.Start();
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// This should be called after the Activity starts and only for root activity of a request.
        /// </summary>
        /// <param name="context">Context to save context to.</param>
        /// <param name="activity">Activity to save.</param>
        private static void SaveCurrentActivity(HttpContext context, Activity activity)
        {
            Debug.Assert(context != null);
            Debug.Assert(activity != null);

            context.Items[ActivityKey] = activity;
        }

        private static void RemoveCurrentActivity(HttpContext context)
        {
            Debug.Assert(context != null);
            context.Items[ActivityKey] = null;
        }
    }
}
