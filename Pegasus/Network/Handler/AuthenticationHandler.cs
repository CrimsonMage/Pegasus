﻿using System;
using NLog;
using Pegasus.Cryptography;
using Pegasus.Database;
using Pegasus.Database.Data;
using Pegasus.Network.Packet;
using Pegasus.Social;

namespace Pegasus.Network.Handler
{
    public static class AuthenticationHandler
    {
        private static readonly Logger log = LogManager.GetCurrentClassLogger();

        [ObjectPacketHandler(ObjectOpcode.Authenticate)]
        public static void HandleAuthenticate(Session session, NetworkObject networkObject)
        {
            void SendAuthenticationError(byte code)
            {
                var errorCode = new NetworkObject();
                errorCode.AddField(0, NetworkObjectField.CreateIntField(code));

                var authenticationError = new NetworkObject();
                authenticationError.AddField(0, NetworkObjectField.CreateIntField((int)ObjectOpcode.AuthenticateError));
                authenticationError.AddField(1, NetworkObjectField.CreateObjectField(errorCode));
                session.EnqueuePacket(new ServerAuthenticationPacket(authenticationError));
            }

            string username    = NetworkObjectField.ReadStringField(networkObject.GetField(2));
            string password    = NetworkObjectField.ReadStringField(networkObject.GetField(3));
            string version     = NetworkObjectField.ReadStringField(networkObject.GetField(5));
            string accountName = NetworkObjectField.ReadStringField(networkObject.GetField(6));

            CharacterObject characterObject = new CharacterObject();
            characterObject.FromNetworkObject(networkObject.GetField(4).ReadObject());

            if (string.IsNullOrWhiteSpace(username)
                || username.Length > 20
                || username == "Anonymous")
            {
                SendAuthenticationError(0);
                return;
            }

            // anonymous login ignored
            if (version != "1.0.1.14")
            {
                SendAuthenticationError(1);
                return;
            }

            AccountInfo accountInfo = DatabaseManager.Database.GetAccount(username);
            if (accountInfo == null)
                accountInfo = DatabaseManager.Database.CreateAccount(username, password, session.Remote.Address.ToString(), Privilege.All);
            else
            {
                // validate existing account
                if (!BCryptProvider.Verify(password, accountInfo.Password))
                {
                    SendAuthenticationError(0);
                    return;
                }
            }

            log.Info($"Account: {accountInfo.Username}, Character: {characterObject.Name} has signed in!");

            DatabaseManager.Database.UpdateAccount(accountInfo.Id, session.Remote.Address.ToString());

            var authentication = new NetworkObject();
            authentication.AddField(0, NetworkObjectField.CreateIntField((int)ObjectOpcode.Authenticate));
            authentication.AddField(1, NetworkObjectField.CreateIntField((int)accountInfo.Privileges));
            session.EnqueuePacket(new ServerAuthenticationPacket(authentication));

            characterObject.Sequence = NetworkManager.SessionSequence.Dequeue();
            session.SignIn(accountInfo, accountName, characterObject);
        }
    }
}
