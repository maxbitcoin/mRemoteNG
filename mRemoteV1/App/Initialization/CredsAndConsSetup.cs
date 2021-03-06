﻿using System;
using System.IO;
using System.Linq;
using mRemoteNG.Connection;
using mRemoteNG.Credential;
using mRemoteNG.Tools;

namespace mRemoteNG.App.Initialization
{
    public class CredsAndConsSetup
    {
        private readonly CredentialServiceFacade _credentialsService;

        public CredsAndConsSetup(CredentialServiceFacade credentialsService)
        {
            if (credentialsService == null)
                throw new ArgumentNullException(nameof(credentialsService));

            _credentialsService = credentialsService;
        }

        public void LoadCredsAndCons()
        {
            if (Settings.Default.FirstStart && !Settings.Default.LoadConsFromCustomLocation && !File.Exists(Runtime.ConnectionsService.GetStartupConnectionFileName()))
                Runtime.ConnectionsService.NewConnections(Runtime.ConnectionsService.GetStartupConnectionFileName());

            LoadCredentialRepositoryList();
            LoadDefaultConnectionCredentials();
            Runtime.LoadConnections();
        }

        private void LoadCredentialRepositoryList()
        {
            _credentialsService.LoadRepositoryList();
        }

        private void LoadDefaultConnectionCredentials()
        {
            var defaultCredId = Settings.Default.ConDefaultCredentialRecord;
            var matchedCredentials = _credentialsService.GetCredentialRecords().Where(record => record.Id.Equals(defaultCredId)).ToArray();
            DefaultConnectionInfo.Instance.CredentialRecordId = matchedCredentials.FirstOrDefault()?.Id.Maybe();
        }
    }
}