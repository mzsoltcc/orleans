using Orleans.Runtime;
using Orleans.TestingHost;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Orleans.Runtime.Configuration;
using TestExtensions;
using Xunit;

namespace Tester.ClientConnectionTests
{
    public class StallConnectionTests : TestClusterPerTest
    {
        private static TimeSpan Timeout = TimeSpan.FromSeconds(10);

        protected override void ConfigureTestCluster(TestClusterBuilder builder)
        {
            builder.Options.InitialSilosCount = 1;
            builder.ConfigureLegacyConfiguration(legacy =>
            {
                legacy.ClusterConfiguration.Globals.OpenConnectionTimeout = Timeout;
                legacy.ClientConfiguration.ResponseTimeout = Timeout;
            });
        }

        [Fact, TestCategory("Functional")]
        public async Task ConnectToGwAfterStallConnectionOpened()
        {
            Socket stalledSocket;
            var gwEndpoint = this.HostedCluster.Primary.GatewayAddress.Endpoint;

            // Close current client connection
            await this.Client.Close();

            // Stall connection to GW
            using (stalledSocket = new Socket(gwEndpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp))
            {
                await stalledSocket.ConnectAsync(gwEndpoint);

                // Try to reconnect to GW
                var stopwatch = Stopwatch.StartNew();
                this.HostedCluster.InitializeClient();
                stopwatch.Stop();

                // Check that we were able to connect before the first connection timeout
                Assert.True(stopwatch.Elapsed < Timeout);

                stalledSocket.Disconnect(true);
            }
        }

        [Fact, TestCategory("Functional")]
        public async Task SiloJoinAfterStallConnectionOpened()
        {
            Socket stalledSocket;
            var siloEndpoint = this.HostedCluster.Primary.SiloAddress.Endpoint;

            // Stall connection to GW
            using (stalledSocket = new Socket(siloEndpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp))
            {
                await stalledSocket.ConnectAsync(siloEndpoint);

                // Try to add a new silo in the cluster
                this.HostedCluster.StartAdditionalSilo();

                // Wait for the silo to join the cluster
                Assert.True(await WaitForClusterSize(2));

                stalledSocket.Disconnect(true);
            }
        }

        private async Task<bool> WaitForClusterSize(int expectedSize)
        {
            var mgmtGrain = this.Client.GetGrain<IManagementGrain>(0);
            var clusterConfig = new ClusterConfiguration();
            var timeout = TestCluster.GetLivenessStabilizationTime(clusterConfig.Globals);
            var stopWatch = Stopwatch.StartNew();
            do
            {
                var hosts = await mgmtGrain.GetHosts();
                if (hosts.Count == expectedSize)
                {
                    stopWatch.Stop();
                    return true;
                }
                await Task.Delay(500);
            }
            while (stopWatch.Elapsed < timeout);
            return false;
        }
    }
}
