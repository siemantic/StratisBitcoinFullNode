﻿using System;
using System.Collections.Generic;
using System.Security;
using Moq;
using NBitcoin;
using Newtonsoft.Json;
using Stratis.Bitcoin.Features.Miner.Interfaces;
using Stratis.Bitcoin.Features.Miner.Models;
using Stratis.Bitcoin.Features.RPC;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.Bitcoin.Features.Wallet.Tests;
using Stratis.Bitcoin.Tests.Logging;
using Xunit;

namespace Stratis.Bitcoin.Features.Miner.Tests
{
    public class MiningRPCControllerTest : LogsTestBase, IClassFixture<MiningRPCControllerFixture>
    {
        private MiningRPCController controller;
        private Mock<IFullNode> fullNode;
        private Mock<IPosMinting> posMinting;
        private Mock<IWalletManager> walletManager;
        private MiningRPCControllerFixture fixture;
        private Mock<IPowMining> powMining;

        public MiningRPCControllerTest(MiningRPCControllerFixture fixture)
        {
            this.fixture = fixture;
            this.powMining = new Mock<IPowMining>();
            this.fullNode = new Mock<IFullNode>();
            this.posMinting = new Mock<IPosMinting>();
            this.walletManager = new Mock<IWalletManager>();

            this.fullNode.Setup(f => f.NodeService<IWalletManager>(false))
                .Returns(this.walletManager.Object);

            this.controller = new MiningRPCController(this.powMining.Object, this.fullNode.Object, this.LoggerFactory.Object, this.walletManager.Object, this.posMinting.Object);
        }


        [Fact]
        public void Generate_BlockCountLowerThanZero_ThrowsRPCServerException()
        {
            Assert.Throws<RPCServerException>(() =>
            {
                this.controller.Generate(-1);
            });
        }

        [Fact]
        public void Generate_NoWalletLoaded_ThrowsRPCServerException()
        {
            Assert.Throws<RPCServerException>(() =>
            {
                this.walletManager.Setup(w => w.GetWalletsNames())
                    .Returns(new List<string>());

                this.controller.Generate(10);
            });
        }

        [Fact]
        public void Generate_WalletWithoutAccount_ThrowsRPCServerException()
        {
            Assert.Throws<RPCServerException>(() =>
            {
                this.walletManager.Setup(w => w.GetWalletsNames())
                    .Returns(new List<string>() {
                        "myWallet"
                    });

                this.walletManager.Setup(w => w.GetAccounts("myWallet"))
                    .Returns(new List<HdAccount>());

                this.controller.Generate(10);
            });
        }

        [Fact]
        public void Generate_UnusedAddressCanBeFoundOnWallet_GeneratesBlocksUsingAddress_ReturnsBlockHeaderHashes()
        {
            this.walletManager.Setup(w => w.GetWalletsNames())
                   .Returns(new List<string>() {
                        "myWallet"
                   });
            this.walletManager.Setup(w => w.GetAccounts("myWallet"))
                .Returns(new List<HdAccount>() {
                    WalletTestsHelpers.CreateAccount("test")
                });
            var address = WalletTestsHelpers.CreateAddress(false);
            this.walletManager.Setup(w => w.GetUnusedAddress(It.IsAny<WalletAccountReference>()))
                .Returns(address);

            this.powMining.Setup(p => p.GenerateBlocks(It.Is<ReserveScript>(r => r.ReserveFullNodeScript == address.Pubkey), 1, int.MaxValue))
                .Returns(new List<NBitcoin.uint256>() {
                    new NBitcoin.uint256(1255632623)
                });

            var result = this.controller.Generate(1);

            Assert.NotEmpty(result);

            Assert.Equal(new NBitcoin.uint256(1255632623), result[0]);
        }

        [Fact]
        public void StartStaking_WalletNotFound_ThrowsWalletException()
        {
            Assert.Throws<WalletException>(() =>
            {
                this.walletManager.Setup(w => w.GetWallet("myWallet"))
                .Throws(new WalletException("Wallet not found."));

                this.controller.StartStaking("myWallet", "password");
            });  
        }
   
        [Fact]
        public void StartStaking_InvalidWalletPassword_ThrowsSecurityException()
        {
            Assert.Throws<SecurityException>(() =>
            {               
                this.walletManager.Setup(w => w.GetWallet("myWallet"))
                  .Returns(this.fixture.wallet);

                this.controller.StartStaking("myWallet", "password");
            });
        }

        [Fact]
        public void StartStaking_ValidWalletAndPassword_StartsStaking_ReturnsTrue()
        {            
            this.walletManager.Setup(w => w.GetWallet("myWallet"))
              .Returns(this.fixture.wallet);

            this.fullNode.Setup(f => f.NodeFeature<MiningFeature>(true))
                .Returns(new MiningFeature(Network.Main, new MinerSettings(), Configuration.NodeSettings.Default(), this.LoggerFactory.Object, this.powMining.Object, this.posMinting.Object, this.walletManager.Object));

            var result = this.controller.StartStaking("myWallet", "password1");

            Assert.True(result);
            this.posMinting.Verify(p => p.Stake(It.Is<PosMinting.WalletSecret>(s => s.WalletName == "myWallet" && s.WalletPassword == "password1")), Times.Exactly(1));
        }

        [Fact]
        public void GetStakingInfo_WithoutPosMinting_ReturnsEmptyStakingInfoModel()
        {
            this.controller = new MiningRPCController(this.powMining.Object, this.fullNode.Object, this.LoggerFactory.Object, this.walletManager.Object, null);

            var result = this.controller.GetStakingInfo(true);

            Assert.Equal(JsonConvert.SerializeObject(new GetStakingInfoModel()), JsonConvert.SerializeObject(result));
        }

        [Fact]
        public void GetStakingInfo_WithPosMinting_ReturnsPosMintingStakingInfoModel()
        {
            this.posMinting.Setup(p => p.GetGetStakingInfoModel())
                .Returns(new GetStakingInfoModel()
                {
                    Enabled = true,
                    CurrentBlockSize = 150000
                }).Verifiable();

            var result = this.controller.GetStakingInfo(true);
            
            Assert.True(result.Enabled);
            Assert.Equal(150000, result.CurrentBlockSize);
            this.posMinting.Verify();
        }

        [Fact]
        public void GetStakingInfo_NotJsonFormat_ThrowsNotImplementedException()
        {
            Assert.Throws<NotImplementedException>(() => {
                this.controller.GetStakingInfo(false);
            });
        }
    }

    public class MiningRPCControllerFixture
    {
        public readonly Wallet.Wallet wallet;

        public MiningRPCControllerFixture()
        {
            this.wallet = WalletTestsHelpers.GenerateBlankWallet("myWallet", "password1");
        }
    }
}
