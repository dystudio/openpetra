﻿//
// DO NOT REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
//
// @Authors:
//       wolfgangu
//
// Copyright 2004-2010 by OM International
//
// This file is part of OpenPetra.org.
//
// OpenPetra.org is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// OpenPetra.org is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with OpenPetra.org.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ict.Testing.NUnitForms;
using Ict.Petra.Server.MFinance.GL;


using Ict.Common;


namespace Ict.Testing.Petra.Server.MFinance.GL
{
    /// <summary>
    /// Tests for some common Tools.
    /// </summary>
    [TestFixture]
    public partial class TestGLCommonTools : CommonNUnitFunctions
    {
        int LedgerNumber = 43;
        /// <summary>
        /// This routine tests the TLedgerInitFlagHandler completely. It's the routine
        /// which writes "boolean" values to a data base table.
        /// </summary>
        [Test]
        public void Test_01_TLedgerInitFlagHandler()
        {
            bool blnOld = new TLedgerInitFlagHandler(43, TLedgerInitFlagEnum.Revaluation).Flag;

            new TLedgerInitFlagHandler(LedgerNumber, TLedgerInitFlagEnum.Revaluation).Flag = true;
            Assert.IsTrue(new TLedgerInitFlagHandler(
                    LedgerNumber, TLedgerInitFlagEnum.Revaluation).Flag, "Flag was set a line before");
            new TLedgerInitFlagHandler(LedgerNumber, TLedgerInitFlagEnum.Revaluation).Flag = true;
            Assert.IsTrue(new TLedgerInitFlagHandler(
                    LedgerNumber, TLedgerInitFlagEnum.Revaluation).Flag, "Flag was set a line before");
            new TLedgerInitFlagHandler(LedgerNumber, TLedgerInitFlagEnum.Revaluation).Flag = false;
            Assert.IsFalse(new TLedgerInitFlagHandler(
                    LedgerNumber, TLedgerInitFlagEnum.Revaluation).Flag, "Flag was reset a line before");
            new TLedgerInitFlagHandler(LedgerNumber, TLedgerInitFlagEnum.Revaluation).Flag = false;
            Assert.IsFalse(new TLedgerInitFlagHandler(
                    LedgerNumber, TLedgerInitFlagEnum.Revaluation).Flag, "Flag was reset a line before");
            new TLedgerInitFlagHandler(LedgerNumber, TLedgerInitFlagEnum.Revaluation).Flag = blnOld;
        }

        /// <summary>
        /// Test of the THandleLedgerInfo Routine...
        /// </summary>
        [Test]
        public void Test_02_THandleLedgerInfo()
        {
            Assert.AreEqual("EUR", new THandleLedgerInfo(LedgerNumber).BaseCurrency,
                String.Format("Base Currency of {0} shall be EUR", LedgerNumber));
            Assert.AreEqual("5003", new THandleLedgerInfo(LedgerNumber).RevaluationAccount,
                String.Format("Revaluation Account of {0} shall be 5003", LedgerNumber));
        }

        [Test]
        public void Test_03_GetAccountingPeriodInfo()
        {
            GetAccountingPeriodInfo getAPI = new GetAccountingPeriodInfo(LedgerNumber);

            Assert.AreNotEqual(DateTime.MinValue, getAPI.GetPeriodEndDate(1),
                "DateTime.MinValue is an error representative");
            Assert.AreNotEqual(DateTime.MinValue, getAPI.GetPeriodStartDate(1),
                "DateTime.MinValue is an error representative");

            Assert.AreEqual(DateTime.MinValue, getAPI.GetPeriodEndDate(33),
                "DateTime.MinValue is an error representative");
            Assert.AreEqual(DateTime.MinValue, getAPI.GetPeriodEndDate(33),
                "DateTime.MinValue is an error representative");
            Assert.IsTrue(TryGetAccountPeriodInfo(LedgerNumber, 1),
                "This request shall pass");
            Assert.IsFalse(TryGetAccountPeriodInfo(LedgerNumber, 100),
                "This request shall fail (period does not exist)");
        }

        /// <summary>
        /// Test for the internal format converter.
        /// </summary>
        [Test]
        public void Test_04_FormatConverter()
        {
            Assert.AreEqual(2, new FormatConverter(",>>>,>>9.99").digits, "Number of digits: 2");
            Assert.AreEqual(1, new FormatConverter(",>>>,>>9.9").digits, "Number of digits: 1");
            Assert.AreEqual(0, new FormatConverter(",>>>,>>9").digits, "Number of digits: 0");
            try
            {
                decimal d = new FormatConverter("nonsens").digits;
                Assert.Fail("No InternalException thrown");
            }
            catch (TerminateException internalException)
            {
                Assert.AreEqual("GetCurrencyInfo.03", internalException.ErrorCode, "Wrong Error Code");
            }
            catch (Exception)
            {
                Assert.Fail("Other than InternalException thrown");
            }
        }

        /// <summary>
        /// Test of the internal routine GetCurrencyInfo
        /// </summary>
        [Test]
        public void Test_05_GetCurrencyInfo()
        {
            Assert.AreEqual(2, new GetCurrencyInfo("EUR").digits, "Number of digits: 2");
            try
            {
                decimal d = new GetCurrencyInfo("JPN").digits;
                Assert.Fail("No InternalException thrown");
            }
            catch (TerminateException internalException)
            {
                Assert.AreEqual("GetCurrencyInfo.02", internalException.ErrorCode, "Wrong Error Code");
            }
            catch (Exception)
            {
                Assert.Fail("Other than InternalException thrown");
            }

            try
            {
                decimal d = new GetCurrencyInfo("DMG").digits;
                Assert.Fail("No InternalException thrown");
            }
            catch (TerminateException internalException)
            {
                if (internalException.ErrorCode.Equals("GetCurrencyInfo.01"))
                {
                    Assert.Fail("Test Data are not loaded correctly");
                }
                else if (internalException.ErrorCode.Equals("GetCurrencyInfo.02"))
                {
                    Assert.Pass("DMG-Test ok");
                }
                else
                {
                    Assert.AreEqual("GetCurrencyInfo.02",
                        internalException.ErrorCode,
                        "Wrong Error Code");
                }
            }
            catch (Exception)
            {
                Assert.Fail("Other than InternalException thrown");
            }
        }

        /// <summary>
        /// CurrencyInfo supports two currencies and the conversion rules incluing the
        /// rouding based on
        /// </summary>
        [Test]
        public void Test_05_GetCurrencyInfo_2()
        {
            GetCurrencyInfo getCurrencyInfo = new GetCurrencyInfo("EUR", "JPY");

            Assert.AreEqual(1.23m, getCurrencyInfo.RoundBaseCurrencyValue(1.23456m), "Round to 2 digits");
            Assert.AreEqual(1.0m, getCurrencyInfo.RoundForeignCurrencyValue(1.23456m), "Round to 0 digits");

            decimal exchangeRate = 1 / 119.7295m;

            Assert.AreEqual(0.84m, getCurrencyInfo.ToBaseValue(100.00m, exchangeRate),
                "Conversion from 100 YEN to 0.83 EUR");
            Assert.AreEqual(11973, getCurrencyInfo.ToForeignValue(100.00m, exchangeRate),
                "Conversion from 100 EUR to 11983 YEN");
            Assert.AreEqual(120, getCurrencyInfo.ToForeignValue(1.00m, exchangeRate),
                "Conversion from 1 EUR to 120 YEN");

            getCurrencyInfo.ForeignCurrencyCode = "GBP";     // Change foreign Currency to Pound ...
            exchangeRate = 1 / 0.8801m;

            Assert.AreEqual(113.62m, getCurrencyInfo.ToBaseValue(100.00m, exchangeRate),
                "Conversion from 100 GBP to 113.62 EUR");
            Assert.AreEqual(88.01m, getCurrencyInfo.ToForeignValue(100.00m, exchangeRate),
                "Conversion from 100 EUR to 88.01 GBP");
        }

        /// <summary>
        /// Test of the Lock-System for ledgers ...
        /// </summary>
        [Test]
        public void Test_06_TLedgerLock()
        {
            TLedgerLock tLegerLock1 = new TLedgerLock(LedgerNumber);

            Assert.IsTrue(tLegerLock1.IsLocked, "Leger can be locked");
            TLedgerLock tLegerLock2 = new TLedgerLock(LedgerNumber);
            Assert.IsFalse(tLegerLock2.IsLocked, "Leger cannot be locked");
            System.Diagnostics.Debug.WriteLine(tLegerLock2.LockInfo());
            tLegerLock2.UnLock();
        }

        [Test]
        public void Test_07_ProcessStatus()
        {
            THandleLedgerInfo ledgerInfo = new THandleLedgerInfo(LedgerNumber);

            ledgerInfo.YearEndProcessStatus = (int)YearEndProcessStatus.ACCOUNT_CLOSED_OUT;
            Assert.AreEqual((int)YearEndProcessStatus.ACCOUNT_CLOSED_OUT, ledgerInfo.YearEndProcessStatus,
                "OK");
            ledgerInfo.YearEndProcessStatus = (int)YearEndProcessStatus.GIFT_CLOSED_OUT;
            Assert.AreEqual((int)YearEndProcessStatus.GIFT_CLOSED_OUT, ledgerInfo.YearEndProcessStatus,
                "OK");
        }

        /// <summary>
        /// Test of the Routines HasNoChilds and ChildList
        /// of GetAccountHierarchyDetailInfo
        /// </summary>
        [Test]
        public void Test_08_GetAccountHierarchyDetailInfo()
        {
            GetAccountHierarchyDetailInfo gahdi = new GetAccountHierarchyDetailInfo(
                new THandleLedgerInfo(LedgerNumber));

            Assert.IsTrue(gahdi.HasNoChilds("6800"), "Base Account without childs");
            Assert.IsFalse(gahdi.HasNoChilds("6800S"), "Root Account");
            IList <String>list = gahdi.ChildList("7000S");
            Assert.AreEqual(2, list.Count, "Two entries ...");
            Assert.AreEqual("7000", list[0], "7000 is the first account");
            Assert.AreEqual("7010", list[1], "7010 is the second account");
            Assert.AreEqual("7000S", gahdi.GetParentAccount("7010"));
        }

        private bool TryGetAccountPeriodInfo(int ALedgerNum, int APeriodNum)
        {
            try
            {
                GetAccountingPeriodInfo getAPI = new GetAccountingPeriodInfo(ALedgerNum, APeriodNum);
                DateTime date = getAPI.PeriodEndDate;
                return date != null;
            }
            catch (IndexOutOfRangeException)
            {
                return false;
            }
        }

        [TestFixtureSetUp]
        public void Init()
        {
            InitServerConnection();
        }

        [TestFixtureTearDown]
        public void TearDown()
        {
            DisconnectServerConnection();
        }
    }
}