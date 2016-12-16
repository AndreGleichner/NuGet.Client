﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.ComponentModel.Composition;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using NuGet.Configuration;
using NuGet.PackageManagement.VisualStudio;
using NuGet.Protocol.Core.Types;
using Task = System.Threading.Tasks.Task;

namespace NuGet.SolutionRestoreManager
{
    /// <summary>
    /// Visual Studio extension package designed to bootstrap solution restore components.
    /// Loads on solution open to attach to build events.
    /// </summary>
    // Flag AllowsBackgroundLoading is set to False because switching to Main thread wiht JTF is creating
    // performance overhead in InitializeAsync() API.
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string)]
    [Guid(PackageGuidString)]
    public sealed class RestoreManagerPackage : AsyncPackage
    {
        public const string ProductVersion = "4.0.0";

        /// <summary>
        /// RestoreManagerPackage GUID string.
        /// </summary>
        public const string PackageGuidString = "2b52ac92-4551-426d-bd34-c6d7d9fdd1c5";

        [Import]
        private ISolutionRestoreWorker SolutionRestoreWorker { get; set; }

        [Import]
        private Lazy<ISettings> Settings { get; set; }

        [Import]
        private IVsSolutionManager SolutionManager { get; set; }

        // keeps a reference to BuildEvents so that our event handler
        // won't get disconnected.
        private EnvDTE.BuildEvents _buildEvents;

        // keep a reference to initialize task so that it can complete
        // even after InitializeAsync() is done.
        private JoinableTask _initializationTask;

        protected override async Task InitializeAsync(
            CancellationToken cancellationToken, 
            IProgress<ServiceProgressData> progress)
        {
            var componentModel = await GetServiceAsync(typeof(SComponentModel)) as IComponentModel;
            componentModel.DefaultCompositionService.SatisfyImportsOnce(this);

            // Start this initialization task but don't await since it will degrade performance.
            // will make sure that this piece is always executed on Main thread.
            // Besides we don't need to wait here to complete since this will only bind to build event which
            _initializationTask = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    var dte = (EnvDTE.DTE)await GetServiceAsync(typeof(SDTE));
                    _buildEvents = dte.Events.BuildEvents;
                    _buildEvents.OnBuildBegin += BuildEvents_OnBuildBegin;

                    UserAgent.SetUserAgentString(
                        new UserAgentStringBuilder().WithVisualStudioSKU(dte.GetFullVsVersionString()));
                }
                catch (Exception ex)
                {
                    // log error to activity logs.
                    ExceptionHelper.WriteToActivityLog(ex);
                }
            });
 
            await base.InitializeAsync(cancellationToken, progress);
        }

        private void BuildEvents_OnBuildBegin(
            EnvDTE.vsBuildScope scope, EnvDTE.vsBuildAction Action)
        {
            if (Action == EnvDTE.vsBuildAction.vsBuildActionClean)
            {
                // Clear the project.json restore cache on clean to ensure that the next build restores again
                SolutionRestoreWorker.CleanCache();

                return;
            }

            // Check if solution is DPL enabled, then don't restore
            if (SolutionManager.IsSolutionDPLEnabled)
            {
                return;
            }

            if (!ShouldRestoreOnBuild)
            {
                return;
            }

            var forceRestore = Action == EnvDTE.vsBuildAction.vsBuildActionRebuildAll;

            // Execute
            SolutionRestoreWorker.Restore(SolutionRestoreRequest.OnBuild(forceRestore));
        }

        /// <summary>
        /// Returns true if automatic package restore on build is enabled.
        /// </summary>
        private bool ShouldRestoreOnBuild
        {
            get
            {
                var packageRestoreConsent = new PackageRestoreConsent(Settings.Value);
                return packageRestoreConsent.IsAutomatic;
            }
        }
    }
}
