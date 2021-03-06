﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Web;

namespace Microsoft.AspNet.TelemetryCorrelation
{
    /// <summary>
    /// Http Module sets ambient state using Activity API from DiagnosticsSource package.
    /// </summary>
    public class TelemetryCorrelationHttpModule : IHttpModule
    {
        private const string BeginCalledFlag = "Microsoft.AspNet.TelemetryCorrelation.BeginCalled";

        /// <inheritdoc />
        public void Dispose()
        {
        }

        /// <inheritdoc />
        public void Init(HttpApplication context)
        {
            context.BeginRequest += Application_BeginRequest;
            context.EndRequest += Application_EndRequest;
            context.PreRequestHandlerExecute += Application_PreRequestHandlerExecute;
        }

        private void Application_BeginRequest(object sender, EventArgs e)
        {
            var context = ((HttpApplication)sender).Context;
            AspNetTelemetryCorrelationEventSource.Log.TraceCallback("Application_BeginRequest");
            ActivityHelper.CreateRootActivity(context);
            context.Items[BeginCalledFlag] = true;
        }

        private void Application_PreRequestHandlerExecute(object sender, EventArgs e)
        {
            AspNetTelemetryCorrelationEventSource.Log.TraceCallback("Application_PreRequestHandlerExecute");
            var context = ((HttpApplication)sender).Context;

            var rootActivity = (Activity)context.Items[ActivityHelper.ActivityKey];
            if (Activity.Current == null && rootActivity != null)
            {
                ActivityHelper.RestoreCurrentActivity(rootActivity);
            }
        }

        private void Application_EndRequest(object sender, EventArgs e)
        {
            AspNetTelemetryCorrelationEventSource.Log.TraceCallback("Application_EndRequest");

            var context = ((HttpApplication)sender).Context;

            // EndRequest does it's best effort to notify that request has ended
            // BeginRequest has never been called
            if (!context.Items.Contains(BeginCalledFlag))
            {
                // Activity has never been started
                var activity = ActivityHelper.CreateRootActivity(context);
                ActivityHelper.StopAspNetActivity(activity, context);
            }
            else
            {
                var activity = (Activity)context.Items[ActivityHelper.ActivityKey];

                // try to stop activity if it's in the Current stack
                if (!ActivityHelper.StopAspNetActivity(activity, context))
                {
                    // Activity we created was lost, let's report it
                    if (activity != null)
                    {
                        ActivityHelper.StopLostActivity(activity, context);
                    }
                }
            }
        }
    }
}
