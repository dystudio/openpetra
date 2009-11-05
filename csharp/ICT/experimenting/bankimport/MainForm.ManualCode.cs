﻿/*************************************************************************
 *
 * DO NOT REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
 *
 * @Authors:
 *       timop
 *
 * Copyright 2004-2009 by OM International
 *
 * This file is part of OpenPetra.org.
 *
 * OpenPetra.org is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * OpenPetra.org is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with OpenPetra.org.  If not, see <http://www.gnu.org/licenses/>.
 *
 ************************************************************************/
using System;
using System.Data;
using System.Windows.Forms;
using System.Collections.Generic;
using System.IO;
using Mono.Unix;
using Ict.Common;
using Ict.Common.Printing;
using Ict.Plugins.Finance.SwiftParser;
using Ict.Petra.Shared.MFinance.Account.Data;
using Ict.Petra.Shared.MFinance.Gift.Data;

namespace Ict.Petra.Client.MFinance.Gui.BankImport
{
    partial class TFrmMainForm
    {
        private IImportBankStatement FBankStatementImporter;
        private BankImportTDS FMainDS;

        private void InitializeManualCode()
        {
            FMainDS = new BankImportTDS();
            FBankStatementImporter = new TImportMT940();

            try
            {
                TGetData.InitDBConnection();
            }
            catch (Exception exp)
            {
                MessageBox.Show(exp.Message, Catalog.GetString("Error connecting to Petra 2.x"));
            }
        }

        /// <summary>
        /// import each statement that has not been posted yet,
        /// and export 3 csv files: one for matched gifts, one for unmatched, one for other
        /// </summary>
        private void ProcessAllNewStatements(Object sender, EventArgs e)
        {
            try
            {
                this.Cursor = Cursors.WaitCursor;
                string[] newFiles = Directory.GetFiles((TAppSettingsManager.GetValueStatic("MT940.Output.Path") +
                                                        Path.DirectorySeparatorChar +
                                                        TAppSettingsManager.GetValueStatic("LegalEntity")).Replace("\\\\",
                        "\\"), "*.sta");

                foreach (string newFile in newFiles)
                {
                    FMainDS.AEpTransaction.Rows.Clear();
                    FMainDS.AGiftDetail.Rows.Clear();
                    FMainDS.PBankingDetails.Rows.Clear();

                    double startBalance, endBalance;
                    string bankName;
                    FBankStatementImporter.ImportFromFile(newFile, ref FMainDS, out startBalance, out endBalance, out bankName);

                    AutoMatchGiftsAgainstPetraDB();

                    rbtMatchedGifts.Checked = true;
                    TGiftMatching exportMatchGifts = new TGiftMatching();
                    exportMatchGifts.WritePetraImportFile(ref FMainDS,
                        TAppSettingsManager.GetValueStatic("OutputCSV.Path") +
                        Path.DirectorySeparatorChar + Path.GetFileNameWithoutExtension(newFile) +
                        Catalog.GetString("Matched") + ".csv",
                        bankName);

                    rbtUnmatchedGifts.Checked = true;
                    StreamWriter sw = new StreamWriter(TAppSettingsManager.GetValueStatic("OutputCSV.Path") +
                        Path.DirectorySeparatorChar + Path.GetFileNameWithoutExtension(newFile) +
                        Catalog.GetString("Unmatched") + ".csv",
                        false, System.Text.Encoding.Default);

                    TGiftMatching.WriteCSVFile(ref FMainDS, sw);

                    sw.Close();

                    rbtOther.Checked = true;
                    sw = new StreamWriter(TAppSettingsManager.GetValueStatic("OutputCSV.Path") +
                        Path.DirectorySeparatorChar + Path.GetFileNameWithoutExtension(newFile) +
                        Catalog.GetString("Other") + ".csv",
                        false, System.Text.Encoding.Default);

                    TGiftMatching.WriteCSVFile(ref FMainDS, sw);

                    sw.Close();
                }

                FMainDS.AEpTransaction.Rows.Clear();
                FMainDS.AGiftDetail.Rows.Clear();
                FMainDS.PBankingDetails.Rows.Clear();

                this.Cursor = Cursors.Default;

                MessageBox.Show(String.Format(Catalog.GetString("Please check the files in {0}"), TAppSettingsManager.GetValueStatic("OutputCSV.Path")));
            }
            catch (Exception exp)
            {
                this.Cursor = Cursors.Default;
                MessageBox.Show(exp.Message);
            }
        }

        private void ImportStatement(Object sender, EventArgs e)
        {
            OpenFileDialog DialogOpen = new OpenFileDialog();

            DialogOpen.Filter = Catalog.GetString(FBankStatementImporter.GetFileFilter());
            DialogOpen.RestoreDirectory = true;
            DialogOpen.InitialDirectory = (TAppSettingsManager.GetValueStatic("MT940.Output.Path") +
                                           Path.DirectorySeparatorChar + TAppSettingsManager.GetValueStatic("LegalEntity")).Replace("\\\\", "\\");
            DialogOpen.Title = Catalog.GetString("Import bank statement file");

            if (DialogOpen.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    // TODO: for some reason, the columns' initialisation in the constructor does not have any effect; need to do here again???
                    grdResult.Columns.Clear();
                    grdResult.AddTextColumn("transaction type", FMainDS.AEpTransaction.ColumnTransactionTypeCode);
                    grdResult.AddTextColumn("Account Name", FMainDS.AEpTransaction.ColumnAccountName);
                    grdResult.AddTextColumn("DonorKey", FMainDS.AEpTransaction.ColumnDonorKey);
                    grdResult.AddTextColumn("DonorShortName", FMainDS.AEpTransaction.ColumnDonorShortName);
                    grdResult.AddTextColumn("Account Number", FMainDS.AEpTransaction.ColumnBankAccountNumber);
                    grdResult.AddTextColumn("description", FMainDS.AEpTransaction.ColumnDescription);
                    grdResult.AddTextColumn("Recipient", FMainDS.AEpTransaction.ColumnRecipientDescription);
                    grdResult.AddTextColumn("Transaction Amount", FMainDS.AEpTransaction.ColumnTransactionAmount);

                    FMainDS.AEpTransaction.Rows.Clear();
                    FMainDS.AGiftDetail.Rows.Clear();
                    FMainDS.PBankingDetails.Rows.Clear();

                    // TODO: at the moment only support one statement by file?
                    double startBalance, endBalance;
                    string bankName;
                    FBankStatementImporter.ImportFromFile(DialogOpen.FileName, ref FMainDS, out startBalance, out endBalance, out bankName);

                    AutoMatchGiftsAgainstPetraDB();

                    FillPanelInfo(startBalance, endBalance, bankName);

                    rbtMatchedGifts.Checked = true;

                    FMainDS.AEpTransaction.DefaultView.AllowNew = false;
                    grdResult.DataSource = new DevAge.ComponentModel.BoundDataView(FMainDS.AEpTransaction.DefaultView);
                    grdResult.AutoSizeCells();
                }
                catch (Exception exp)
                {
                    MessageBox.Show(exp.Message + Environment.NewLine + exp.StackTrace, Catalog.GetString("Error importing bank statement"));
                }
            }
        }

        private void SetFilterMatchingGifts()
        {
            FMainDS.AEpTransaction.DefaultView.RowFilter = AEpTransactionTable.GetMatchingStatusDBName() + "= '" +
                                                           Ict.Petra.Shared.MFinance.MFinanceConstants.BANK_STMT_STATUS_MATCHED + "' AND " +
                                                           AEpTransactionTable.GetTransactionAmountDBName().ToString(
                System.Globalization.CultureInfo.InvariantCulture) + " > 0 AND " + AEpTransactionTable.GetEpMatchKeyDBName() + " IS NOT NULL";
        }

        private void SetFilterUnmatchedGifts()
        {
            FMainDS.AEpTransaction.DefaultView.RowFilter = "(" + AEpTransactionTable.GetMatchingStatusDBName() + " <> " +
                                                           "'" + Ict.Petra.Shared.MFinance.MFinanceConstants.BANK_STMT_STATUS_MATCHED + "'" +
                                                           " OR " +
                                                           AEpTransactionTable.GetEpMatchKeyDBName() + " IS NULL) AND " +
                                                           AEpTransactionTable.GetTransactionAmountDBName().ToString(
                System.Globalization.CultureInfo.InvariantCulture) + " > 0 AND (" +
                                                           FBankStatementImporter.GetFilterGifts() + ")";
        }

        private void SetFilterOther()
        {
            FMainDS.AEpTransaction.DefaultView.RowFilter = AEpTransactionTable.GetMatchingStatusDBName() + " IS NULL AND (" +
                                                           AEpTransactionTable.GetTransactionAmountDBName().ToString(
                System.Globalization.CultureInfo.InvariantCulture) + " < 0 OR NOT (" +
                                                           FBankStatementImporter.GetFilterGifts() + "))";
        }

        private void FilterChanged(Object sender, EventArgs e)
        {
            if (FMainDS == null)
            {
                return;
            }

            if (rbtAllTransactions.Checked)
            {
                FMainDS.AEpTransaction.DefaultView.RowFilter = "";
            }
            else if (rbtMatchedGifts.Checked)
            {
                SetFilterMatchingGifts();
            }
            else if (rbtUnmatchedGifts.Checked)
            {
                SetFilterUnmatchedGifts();
            }
            else if (rbtOther.Checked)
            {
                SetFilterOther();
            }

            //MessageBox.Show(FMainDS.AEpTransaction.DefaultView.RowFilter);
        }

        // determine the one gift batch that was posted for this bank statement
        private Int32 FSelectedGiftBatch = -1;

        private bool AutoMatchGiftsAgainstPetraDB()
        {
            FSelectedGiftBatch = TGiftMatching.AutoMatchGiftsAgainstPetraDB(ref FMainDS);

            if (FSelectedGiftBatch == -1)
            {
                txtValueMatchedGiftBatch.Visible = false;

                TGiftMatching matchGifts = new TGiftMatching();
                return matchGifts.FindMatches(ref FMainDS);
            }

            // TODO: checksum of SelectedGiftBatch and matched transactions; move other transactions to Other state?

            // log all gifts in the gift batch that have not been matched
            FMainDS.AGiftDetail.DefaultView.RowFilter = BankImportTDSAGiftDetailTable.GetBatchNumberDBName() + " = " +
                                                        FSelectedGiftBatch.ToString() +
                                                        " AND AlreadyMatched = false";
            double SumUnmatched = 0.0;
            double SumMatched = 0.0;

            if (FMainDS.AGiftDetail.DefaultView.Count > 0)
            {
                TLogging.Log("The following gifts in batch " + FSelectedGiftBatch.ToString() + " have not been matched: ");

                foreach (DataRowView rv in FMainDS.AGiftDetail.DefaultView)
                {
                    BankImportTDSAGiftDetailRow detailrow = (BankImportTDSAGiftDetailRow)rv.Row;
                    TLogging.Log(
                        detailrow.GiftTransactionAmount.ToString() + "; " + detailrow.DonorShortName + " " + detailrow.DonorKey.ToString() + " " +
                        detailrow.RecipientDescription);
                    SumUnmatched += detailrow.GiftTransactionAmount;
                }
            }

            FMainDS.AGiftDetail.DefaultView.RowFilter = BankImportTDSAGiftDetailTable.GetBatchNumberDBName() + " = " +
                                                        FSelectedGiftBatch.ToString() +
                                                        " AND AlreadyMatched = true";

            foreach (DataRowView rv in FMainDS.AGiftDetail.DefaultView)
            {
                BankImportTDSAGiftDetailRow detailrow = (BankImportTDSAGiftDetailRow)rv.Row;
                SumMatched += detailrow.GiftTransactionAmount;
            }

            // Test: SumMatched should be the same as SumCredit of matched gift view
            Int32 countRows;
            SetFilterMatchingGifts();
            double sumCreditMatched, sumDebitMatched;
            CalculateSumsFromTransactionView(out sumCreditMatched, out sumDebitMatched, out countRows);

            txtValueMatchedGiftBatch.Visible = false;

            if (Convert.ToDecimal(sumCreditMatched) != Convert.ToDecimal(SumMatched))
            {
                txtValueMatchedGiftBatch.Visible = true;
                txtValueMatchedGiftBatch.Text = SumMatched.ToString();
                txtValueMatchedGiftBatch.BackColor = System.Drawing.Color.Red;
                TLogging.Log(String.Format("Sum of matched gift details: {0}; sum of unmatched gifts: {1}; value of gift batch {2}: {3}", SumMatched,
                        SumUnmatched, FSelectedGiftBatch, SumMatched + SumUnmatched));
                MessageBox.Show(String.Format(Catalog.GetString(
                            "There is a problem: matched gifts from gift batch are {0}, but matched gifts from bank statement are {1}"), SumMatched,
                        sumCreditMatched));
            }

            // TODO: export a list of mismatching account numbers

            return true;
        }

        private void CalculateSumsFromTransactionView(out double ASumCredit, out double ASumDebit, out Int32 ACount)
        {
            ASumCredit = 0.0;
            ASumDebit = 0.0;

            ACount = FMainDS.AEpTransaction.DefaultView.Count;

            foreach (DataRowView rv in FMainDS.AEpTransaction.DefaultView)
            {
                double amount = ((AEpTransactionRow)rv.Row).TransactionAmount;

                if (amount > 0)
                {
                    ASumCredit += amount;
                }
                else
                {
                    ASumDebit += amount;
                }
            }
        }

        private void FillPanelInfo(double startBalance, double endBalance, string ABankName)
        {
            Int32 countRows;

            // use the last transaction for the date effective, sometimes the first transaction is from a previous date (eg. Saturday, Sunday)
            txtDateStatement.Text = FMainDS.AEpTransaction[FMainDS.AEpTransaction.Rows.Count - 1].DateEffective.ToShortDateString();
            txtBankName.Text = ABankName;
            txtStartBalance.Text = startBalance.ToString();
            txtEndBalance.Text = endBalance.ToString();

            SetFilterMatchingGifts();
            double sumCreditMatched, sumDebitMatched;
            CalculateSumsFromTransactionView(out sumCreditMatched, out sumDebitMatched, out countRows);
            txtNumberMatched.Text = countRows.ToString();
            txtValueMatchedGifts.Text = sumCreditMatched.ToString();

            if (sumDebitMatched > 0)
            {
                MessageBox.Show(Catalog.GetString("Problems with import, there should be no debits in gifts"));
            }

            SetFilterUnmatchedGifts();
            double sumCreditUnmatched, sumDebitUnmatched;
            CalculateSumsFromTransactionView(out sumCreditUnmatched, out sumDebitUnmatched, out countRows);
            txtNumberUnmatched.Text = countRows.ToString();
            txtValueUnmatchedGifts.Text = sumCreditUnmatched.ToString();

            if (sumDebitUnmatched > 0)
            {
                MessageBox.Show(Catalog.GetString("Problems with import, there should be no debits in gifts"));
            }

            SetFilterOther();
            double sumCreditOther, sumDebitOther;
            CalculateSumsFromTransactionView(out sumCreditOther, out sumDebitOther, out countRows);
            txtNumberOther.Text = countRows.ToString();
            txtValueOtherCredit.Text = sumCreditOther.ToString();
            txtValueOtherDebit.Text = sumDebitOther.ToString();

            FMainDS.AEpTransaction.DefaultView.RowFilter = "";
            double sumCreditAll, sumDebitAll;
            CalculateSumsFromTransactionView(out sumCreditAll, out sumDebitAll, out countRows);
            txtNumberAltogether.Text = countRows.ToString();
            txtSumCredit.Text = sumCreditAll.ToString();
            txtSumDebit.Text = sumDebitAll.ToString();

            if (Convert.ToDecimal(startBalance + sumCreditAll + sumDebitAll) != Convert.ToDecimal(endBalance))
            {
                MessageBox.Show(Catalog.GetString("the startbalance, credit/debit all and endbalance don't add up"));
            }

            if (Convert.ToDecimal(sumCreditAll) != Convert.ToDecimal(sumCreditMatched + sumCreditUnmatched + sumCreditOther))
            {
                MessageBox.Show(Catalog.GetString("the credits don't add up"));
            }

            if (Convert.ToDecimal(sumDebitAll) != Convert.ToDecimal(sumDebitMatched + sumDebitUnmatched + sumDebitOther))
            {
                MessageBox.Show(Catalog.GetString("the debits don't add up"));
            }
        }

        private void SplitAndTrain(object sender, EventArgs e)
        {
            try
            {
                this.Cursor = Cursors.WaitCursor;
                TImportMT940.SplitFilesAndMove();
                TGiftMatching.Training(TAppSettingsManager.GetValueStatic("LegalEntity"), FBankStatementImporter);
                this.Cursor = Cursors.Default;
                MessageBox.Show(Catalog.GetString("Splitting and training finished"));
            }
            catch (Exception exp)
            {
                this.Cursor = Cursors.Default;
                MessageBox.Show(exp.Message);
            }
        }

        private void Export(object sender, EventArgs e)
        {
            if (FMainDS.AEpTransaction.DefaultView.Count == 0)
            {
                return;
            }

            if (rbtOther.Checked)
            {
                SaveFileDialog DialogSave = new SaveFileDialog();

                DialogSave.Filter = Catalog.GetString("Other Gifts file (*.csv)|*.csv");
                DialogSave.AddExtension = true;
                DialogSave.RestoreDirectory = true;
                DialogSave.Title = Catalog.GetString("Export list of other transactions");

                if (DialogSave.ShowDialog() == DialogResult.OK)
                {
                    StreamWriter sw = new StreamWriter(DialogSave.FileName, false, System.Text.Encoding.Default);

                    TGiftMatching.WriteCSVFile(ref FMainDS, sw);

                    sw.Close();
                }
            }
            else if (rbtUnmatchedGifts.Checked)
            {
                SaveFileDialog DialogSave = new SaveFileDialog();

                DialogSave.Filter = Catalog.GetString("Unmatched Gifts file (*.csv)|*.csv");
                DialogSave.AddExtension = true;
                DialogSave.RestoreDirectory = true;
                DialogSave.Title = Catalog.GetString("Export list of unmatched gifts");

                if (DialogSave.ShowDialog() == DialogResult.OK)
                {
                    StreamWriter sw = new StreamWriter(DialogSave.FileName, false, System.Text.Encoding.Default);

                    TGiftMatching.WriteCSVFile(ref FMainDS, sw);

                    sw.Close();
                }
            }
            else if (rbtMatchedGifts.Checked && (FSelectedGiftBatch == -1))
            {
                SaveFileDialog DialogSave = new SaveFileDialog();

                DialogSave.Filter = Catalog.GetString("Gift Batch file (*.csv)|*.csv");
                DialogSave.AddExtension = true;
                DialogSave.RestoreDirectory = true;
                DialogSave.Title = Catalog.GetString("Export gift batch of matched gifts");

                if (DialogSave.ShowDialog() == DialogResult.OK)
                {
                    TGiftMatching exportMatchGifts = new TGiftMatching();
                    exportMatchGifts.WritePetraImportFile(ref FMainDS, DialogSave.FileName, txtBankName.Text);
                }
            }
        }

        private void Print(object sender, EventArgs e)
        {
            if (FMainDS.AEpTransaction.DefaultView.Count == 0)
            {
                return;
            }

            System.Drawing.Printing.PrintDocument doc = new System.Drawing.Printing.PrintDocument();
            bool PrinterInstalled = doc.PrinterSettings.IsValid;

            if (!PrinterInstalled)
            {
                MessageBox.Show("The program cannot find a printer, and therefore cannot print!", "Problem with printing");
                return;
            }

            string ShortCodeOfBank = "TODO";
            string DateOfStatement = "TODO";
            string HtmlDocument = String.Empty;

            if (rbtUnmatchedGifts.Checked)
            {
                HtmlDocument =
                    TGiftMatching.PrintHTML(ref FMainDS, Catalog.GetString("Unmatched transactions, " + ShortCodeOfBank + ", " + DateOfStatement));
            }

            if (HtmlDocument.Length == 0)
            {
                MessageBox.Show(Catalog.GetString("nothing to print"));
            }

            TGfxPrinter GfxPrinter = new TGfxPrinter(doc);
            TPrinterHtml htmlPrinter = new TPrinterHtml(HtmlDocument,
                String.Empty,
                GfxPrinter);
            GfxPrinter.Init(eOrientation.ePortrait, htmlPrinter);

            PrintDialog dlg = new PrintDialog();
            dlg.Document = GfxPrinter.Document;
            dlg.AllowCurrentPage = true;
            dlg.AllowSomePages = true;

            if (dlg.ShowDialog() == DialogResult.OK)
            {
                dlg.Document.Print();
            }
        }
    }
}