﻿//
// DO NOT REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
//
// @Authors:
//       berndr
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
//
using System;
using System.Drawing;
using System.Collections;
using System.ComponentModel;
using System.Windows.Forms;
using Mono.Unix;
using Ict.Common;
using Ict.Common.Verification;
using Ict.Common.Controls;
using Ict.Petra.Client.App.Core.RemoteObjects;
using Ict.Petra.Client.MFinance.Logic;
using Ict.Petra.Client.MReporting.Logic;
using Ict.Petra.Shared.MReporting;
using SourceGrid.Selection;

namespace Ict.Petra.Client.MReporting.Gui.MFinance
{
    /// <summary>
    /// Description of TFrmUC_GeneralSettings.
    /// </summary>
    public partial class TFrmUC_GeneralSettings
    {
        private int FLedgerNumber;
        private int FNumberAccountingPeriods;
        private int FNumberForwardingPeriods;
        private int FCurrentPeriod;
        private int FCurrentYear;

        /// <summary>
        /// Initialisation
        /// </summary>
        public void InitialiseData(TFrmPetraReportingUtils APetraUtilsObject)
        {
            rbtPeriod.Checked = true;
            dtpStartDate.Enabled = false;
            dtpEndDate.Enabled = false;
            rbtDate.Enabled = false;
            txtQuarter.Enabled = false;
            cmbQuarterYear.Enabled = false;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        public void InitialiseLedger(int ALedgerNumber)
        {
            FLedgerNumber = ALedgerNumber;

            TRemote.MFinance.Reporting.UIConnectors.SelectLedger(FLedgerNumber);
            TRemote.MFinance.Reporting.UIConnectors.GetLedgerPeriodDetails(out FNumberAccountingPeriods, out FNumberForwardingPeriods,
                out FCurrentPeriod, out FCurrentYear);

            txtLedger.Text = TFinanceControls.GetLedgerNumberAndName(FLedgerNumber);

            int SelectedIndex = cmbPeriodYear.SelectedIndex;
            TFinanceControls.InitialiseAvailableFinancialYearsList(ref cmbPeriodYear, FLedgerNumber);
            cmbPeriodYear.SelectedIndex = SelectedIndex;

            SelectedIndex = cmbQuarterYear.SelectedIndex;
            TFinanceControls.InitialiseAvailableFinancialYearsList(ref cmbQuarterYear, FLedgerNumber);
            cmbQuarterYear.SelectedIndex = SelectedIndex;

            SelectedIndex = cmbAccountHierarchy.SelectedIndex;
            TFinanceControls.InitialiseAccountHierarchyList(ref cmbAccountHierarchy, FLedgerNumber);
            cmbAccountHierarchy.SelectedIndex = SelectedIndex;

            // if there is only one hierarchy, disable the control
//			cmbAccountHierarchy.Enabled = (cmbAccountHierarchy.Count > 1);

            /* select the latest year TODO ??? */
            //            if (this.CbB_AvailableYears.Items.Count > 0)
            //            {
            //                this.CbB_AvailableYears.SelectedIndex = 0; /// first item is the most current year
            //            }
        }

        #region Parameter/Settings Handling

        /// <summary>
        /// Sets the available functions (fields) that can be used for this report.
        /// </summary>
        /// <param name="AAvailableFunctions">List of TColumnFunction</param>
        public void SetAvailableFunctions(ArrayList AAvailableFunctions)
        {
        }

        /// <summary>
        /// Reads the selected values from the controls,
        /// and stores them into the parameter system of FCalculator
        ///
        /// </summary>
        /// <param name="ACalculator"></param>
        /// <param name="AReportAction"></param>
        /// <returns>void</returns>
        public void ReadControls(TRptCalculator ACalculator, TReportActionEnum AReportAction)
        {
            int Year = 0;

            // TODO
            int DiffPeriod = 0;             //(System.Int32)CbB_YearEndsOn.SelectedItem;

            //            DiffPeriod = DiffPeriod - 12;
            ACalculator.AddParameter("param_diff_period_i", DiffPeriod);

            ACalculator.AddParameter("param_account_hierarchy_c", this.cmbAccountHierarchy.GetSelectedString());
            ACalculator.AddParameter("param_currency", this.cmbCurrency.GetSelectedString());

            ACalculator.AddParameter("param_quarter", (System.Object) this.rbtQuarter.Checked);

            if (rbtQuarter.Checked)
            {
                Year = (int)cmbQuarterYear.SelectedItem;
                int Quarter = (Int32)StringHelper.TryStrToInt(txtQuarter.Text, 1);
                ACalculator.AddParameter("param_start_period_i", (System.Object)(Quarter * 3 - 2));
                ACalculator.AddParameter("param_end_period_i", (System.Object)(Quarter * 3));

                //VerificationResult = TFinancialPeriodChecks.ValidQuarter(DiffPeriod, Year, Quarter, "Quarter");
                if (AReportAction == TReportActionEnum.raGenerate)
                {
                    CheckQuarter(Year, Quarter);
                }
            }
            else
            {
                Year = (int)cmbPeriodYear.SelectedItem;
                int StartPeriod = (Int32)StringHelper.TryStrToInt(txtStartPeriod.Text, 1);
                int EndPeriod = (Int32)StringHelper.TryStrToInt(txtEndPeriod.Text, 1);
                ACalculator.AddParameter("param_start_period_i", StartPeriod);
                ACalculator.AddParameter("param_end_period_i", EndPeriod);

                if (AReportAction == TReportActionEnum.raGenerate)
                {
                    CheckPeriod(Year, StartPeriod);
                    CheckPeriod(Year, EndPeriod);

                    if (StartPeriod > EndPeriod)
                    {
                        FPetraUtilsObject.AddVerificationResult(new TVerificationResult(
                                Catalog.GetString("Start Period must not be bigger than End Period."),
                                Catalog.GetString("Invalid Data entered."),
                                TResultSeverity.Resv_Critical));
                    }
                }
            }

            ACalculator.AddParameter("param_year_i", Year);
        }

        /// <summary>
        /// Sets the selected values in the controls, using the parameters loaded from a file
        ///
        /// </summary>
        /// <param name="AParameters"></param>
        /// <returns>void</returns>
        public void SetControls(TParameterList AParameters)
        {
            // TODO
            //          int DiffPeriod = 0;//(System.Int32)CbB_YearEndsOn.SelectedItem;
            //            DiffPeriod = DiffPeriod - 12;
            //            ACalculator.AddParameter("param_diff_period_i", DiffPeriod);

            cmbAccountHierarchy.SetSelectedString(AParameters.Get("param_account_hierarchy_c").ToString());
            cmbCurrency.SetSelectedString(AParameters.Get("param_currency").ToString());
            cmbPeriodYear.SetSelectedInt32(AParameters.Get("param_year_i").ToInt());
            cmbQuarterYear.SetSelectedInt32(AParameters.Get("param_year_i").ToInt());

            rbtQuarter.Checked = AParameters.Get("param_quarter").ToBool();
            rbtPeriod.Checked = !rbtQuarter.Checked;

            txtQuarter.Text = (AParameters.Get("param_end_period_i").ToInt() / 3).ToString();
            txtStartPeriod.Text = AParameters.Get("param_start_period_i").ToString();
            txtEndPeriod.Text = AParameters.Get("param_end_period_i").ToString();
        }

        #endregion

        /// <summary>
        /// Hide all Period Range selcection elements except of the year selection
        /// </summary>
        public void ShowOnlyYearSelection()
        {
            bool IsVisible = false;

            rbtPeriod.Visible = IsVisible;
            rbtQuarter.Visible = IsVisible;
            rbtDate.Visible = IsVisible;
            lblStartPeriod.Visible = IsVisible;
            lblEndPeriod.Visible = IsVisible;
            lblQuarter.Visible = IsVisible;
            lblQuarterYear.Visible = IsVisible;
            lblStartDate.Visible = IsVisible;
            lblEndDate.Visible = IsVisible;
            txtStartPeriod.Visible = IsVisible;
            txtEndPeriod.Visible = IsVisible;
            txtQuarter.Visible = IsVisible;
            cmbQuarterYear.Visible = IsVisible;
            dtpStartDate.Visible = IsVisible;
            dtpEndDate.Visible = IsVisible;
            cmbPeriodYear.Enabled = true;
            cmbPeriodYear.Visible = true;
        }

        private void UnselectAll(System.Object sender, System.EventArgs e)
        {
        }

        /// <summary>
        /// Checks whether the period is inside a valid financial year
        /// </summary>
        /// <param name="AYear">which year is the period in</param>
        /// <param name="APeriodNr">period</param>
        /// <returns></returns>
        private void CheckPeriod(int AYear, int APeriodNr)
        {
            String ValidRange;

            System.Int32 RealPeriod;
            System.Int32 RealYear;

            TRemote.MFinance.Reporting.UIConnectors.GetRealPeriod(0, AYear, APeriodNr, out RealPeriod, out RealYear);

            if (RealYear == FCurrentYear)
            {
                ValidRange = Catalog.GetString("1 and ") + Convert.ToString(FCurrentPeriod + FNumberForwardingPeriods);
            }
            else
            {
                ValidRange = Catalog.GetString("1 and ") + FNumberAccountingPeriods.ToString();
            }

            if ((APeriodNr <= 0)
                || ((RealYear == FCurrentYear) && (RealPeriod > FCurrentPeriod + FNumberForwardingPeriods))
                || ((RealYear < FCurrentYear) && (APeriodNr > FNumberAccountingPeriods)))
            {
                FPetraUtilsObject.AddVerificationResult(new TVerificationResult("",
                        Catalog.GetString("Invalid Period entered.") + Environment.NewLine + Catalog.GetString("Period must be between ") +
                        ValidRange + '.',
                        Catalog.GetString("Invalid Data entered"),
                        "X_0041",
                        TResultSeverity.Resv_Critical));
            }
        }

        /// <summary>
        /// Checks whether the quarter is inside a valid financial year
        /// </summary>
        /// <param name="AYear">which year is the period in</param>
        /// <param name="AQuarter">quarter</param>
        /// <returns></returns>
        private void CheckQuarter(int AYear, int AQuarter)
        {
            String ValidRange;

            System.Int32 RealPeriod;
            System.Int32 RealYear;

            TRemote.MFinance.Reporting.UIConnectors.GetRealPeriod(0, AYear, AQuarter * 3 - 2, out RealPeriod, out RealYear);

            if (RealYear == FCurrentYear)
            {
                ValidRange = Catalog.GetString("1 and ") + Convert.ToString(((FCurrentPeriod + FNumberForwardingPeriods) - 1) / 3 + 1);
            }
            else
            {
                ValidRange = Catalog.GetString("1 and ") + Convert.ToString((FNumberAccountingPeriods - 1) / 3 + 1);
            }

            if ((AQuarter <= 0)
                || ((RealYear == FCurrentYear) && (RealPeriod > FCurrentPeriod + FNumberForwardingPeriods))
                || ((RealYear < FCurrentYear) && (AQuarter > 4)))
            {
                FPetraUtilsObject.AddVerificationResult(new TVerificationResult("",
                        Catalog.GetString("Invalid Quarter entered.") + Environment.NewLine + Catalog.GetString("Quarter must be between ") +
                        ValidRange + '.',
                        Catalog.GetString("Invalid Data entered"),
                        "X_0041",
                        TResultSeverity.Resv_Critical));
            }
        }
    }
}