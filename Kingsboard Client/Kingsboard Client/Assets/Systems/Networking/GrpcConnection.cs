using System;
using Grpc.Net.Client;
using UnityEngine;
using UnityEngine.Networking;

namespace Kingsboard.Networking
{
    /// <summary>
    /// The one gRPC connection to the Kingsboard server, shared by every Handler class.
    /// The server address defaults to <see cref="DefaultServerAddress"/> and can be changed
    /// with the command-line argument --server=https://host:port
    /// </summary>
    public static class GrpcConnection
    {
        public const string DefaultServerAddress = "https://localhost:7116";

        private static GrpcChannel _channel;

        public static string ServerAddress { get; private set; } = DefaultServerAddress;

        /// <summary>The shared channel. Created the first time it's needed.</summary>
        public static GrpcChannel Channel
        {
            get
            {
                if (_channel == null)
                {
                    _channel = CreateChannel();
                }
                return _channel;
            }
        }

        // Runs each time the game starts (including entering Play Mode in the editor)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialise()
        {
            Close();
            Application.quitting -= Close;
            Application.quitting += Close;
        }

        private static GrpcChannel CreateChannel()
        {
            ServerAddress = ReadServerAddress();

            // gRPC needs HTTP/2, which Unity provides through UnityHttpMessageHandler
            UnityHttpMessageHandler handler = new UnityHttpMessageHandler();
            handler.HttpForcedVersion = HttpForcedVersion.HTTP2;

            // A local development server uses a self-signed certificate, so don't reject it
            if (new Uri(ServerAddress).IsLoopback)
            {
                handler.CertificateHandler = new AcceptAnyCertificate();
            }

            Debug.Log($"[Grpc] Connecting to {ServerAddress}");
            return GrpcChannel.ForAddress(ServerAddress, new GrpcChannelOptions()
            {
                HttpHandler = handler,
                DisposeHttpClient = true
            });
        }

        private static string ReadServerAddress()
        {
            const string prefix = "--server=";
            foreach (string arg in Environment.GetCommandLineArgs())
            {
                if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && arg.Length > prefix.Length)
                {
                    return arg.Substring(prefix.Length);
                }
            }
            return DefaultServerAddress;
        }

        private static void Close()
        {
            _channel?.Dispose();
            _channel = null;
        }

        private class AcceptAnyCertificate : CertificateHandler
        {
            protected override bool ValidateCertificate(byte[] certificateData)
            {
                return true;
            }
        }
    }
}
