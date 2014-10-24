//
// DO NOT REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
//
// @Authors:
//       matthiash, timop, dougm
//
// Copyright 2004-2012 by OM International
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
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text;

using Ict.Common;
using Ict.Common.DB;
using Ict.Common.Verification;
using Ict.Petra.Server.App.Core;
using Ict.Petra.Server.MFinance.Account.Data.Access;
using Ict.Petra.Server.MFinance.Gift.Data.Access;
using Ict.Petra.Shared.MFinance.Gift.Validation;
using Ict.Petra.Server.MFinance.GL.WebConnectors;
using Ict.Petra.Shared.MFinance.Validation;
using Ict.Petra.Shared;
using Ict.Petra.Shared.MFinance.Account.Data;
using Ict.Petra.Shared.MFinance.Gift.Data;
using Ict.Petra.Server.MSysMan.Maintenance.SystemDefaults.WebConnectors;


//using Ict.Petra.Server.MFinance.Account.Data.Access;
//using Ict.Petra.Shared.MFinance.Account.Data;


namespace Ict.Petra.Server.MFinance.Gift
{
    /// <summary>
    /// Import a Gift Batch
    /// </summary>
    public class TGiftImporting
    {
        String FDelimiter;
        Int32 FLedgerNumber;
        String FDateFormatString;
        GiftBatchTDS FMainDS;
        CultureInfo FCultureInfoNumberFormat;
        CultureInfo FCultureInfoDate;

        private String FImportLine;
        private String FNewLine;

        private String InferCostCentre(AGiftDetailRow AgiftDetails)
        {
            String costCentre = "";

            if (!Common.Common.HasPartnerCostCentreLink(AgiftDetails.RecipientKey, out costCentre))
            {
                // There's no helpful entry in a_valid_ledger_number - I'll see about using the MotivationDetail.
                AMotivationDetailRow mdRow = FMainDS.AMotivationDetail.NewRowTyped(false);
                mdRow.LedgerNumber = AgiftDetails.LedgerNumber;
                mdRow.MotivationGroupCode = AgiftDetails.MotivationGroupCode;
                mdRow.MotivationDetailCode = AgiftDetails.MotivationDetailCode;
                AMotivationDetailTable tempTbl = null;

                TDBTransaction Transaction = null;
                DBAccess.GDBAccessObj.GetNewOrExistingAutoReadTransaction(IsolationLevel.ReadCommitted,
                    TEnforceIsolationLevel.eilMinimum,
                    ref Transaction,
                    delegate
                    {
                        tempTbl = AMotivationDetailAccess.LoadUsingTemplate(mdRow, Transaction);
                    });

                if (tempTbl.Rows.Count > 0)
                {
                    costCentre = tempTbl[0].CostCentreCode;
                }
            }

            return costCentre;
        }

        /// <summary>
        /// Import Gift batch data
        /// The data file contents from the client is sent as a string, imported in the database
        /// and committed immediately
        /// </summary>
        /// <param name="ARequestParams">Hashtable containing the given params </param>
        /// <param name="AImportString">Big parts of the export file as a simple String</param>
        /// <param name="AMessages">Additional messages to display in a messagebox</param>
        /// <returns>false if error</returns>
        public bool ImportGiftBatches(
            Hashtable ARequestParams,
            String AImportString,
            out TVerificationResultCollection AMessages
            )
        {
            TProgressTracker.InitProgressTracker(DomainManager.GClientID.ToString(),
                Catalog.GetString("Importing Gift Batches"),
                100);

            TProgressTracker.SetCurrentState(DomainManager.GClientID.ToString(),
                Catalog.GetString("Initialising"),
                0);

            AMessages = new TVerificationResultCollection();
            FMainDS = new GiftBatchTDS();
            StringReader sr = new StringReader(AImportString);

            // Parse the supplied parameters
            FDelimiter = (String)ARequestParams["Delimiter"];
            FLedgerNumber = (Int32)ARequestParams["ALedgerNumber"];
            FDateFormatString = (String)ARequestParams["DateFormatString"];
            String NumberFormat = (String)ARequestParams["NumberFormat"];
            FNewLine = (String)ARequestParams["NewLine"];

            // Set culture from parameters
            FCultureInfoNumberFormat = new CultureInfo(NumberFormat.Equals("American") ? "en-US" : "de-DE");
            FCultureInfoDate = new CultureInfo("en-GB");
            FCultureInfoDate.DateTimeFormat.ShortDatePattern = FDateFormatString;

            bool TaxDeductiblePercentageEnabled = Convert.ToBoolean(
                TSystemDefaults.GetSystemDefault(SharedConstants.SYSDEFAULT_TAXDEDUCTIBLEPERCENTAGE, "FALSE"));

            // Initialise our working variables
            TDBTransaction Transaction = null;
            AGiftBatchRow giftBatch = null;
            decimal totalBatchAmount = 0;
            Int32 RowNumber = 0;
            Int32 BatchDetailCounter = 0;
            bool ok = false;

            string ImportMessage = Catalog.GetString("Initialising");

            // Create some validation dictionaries
            TValidationControlsDict ValidationControlsDictBatch = new TValidationControlsDict();
            TValidationControlsDict ValidationControlsDictGift = new TValidationControlsDict();
            TValidationControlsDict ValidationControlsDictGiftDetail = new TValidationControlsDict();

            try
            {
                // This needs to be initialised because we will be calling the method
                TSharedFinanceValidationHelper.GetValidPeriodDatesDelegate = @TAccountingPeriodsWebConnector.GetPeriodDates;

                // Get a new transaction
                Transaction = DBAccess.GDBAccessObj.BeginTransaction(IsolationLevel.Serializable);

                // If we did not succeed there is something wrong (a transaction is already dangling somewhere?)
                if (Transaction == null)
                {
                    throw new Exception(Catalog.GetString(
                            "Could not create a new import transaction because an existing transaction has not completed."));
                }

                // Load supplementary tables that we are going to need for validation
                ALedgerTable LedgerTable = ALedgerAccess.LoadByPrimaryKey(FLedgerNumber, Transaction);
                AAccountTable AccountTable = AAccountAccess.LoadViaALedger(FLedgerNumber, Transaction);
                ACostCentreTable CostCentreTable = ACostCentreAccess.LoadViaALedger(FLedgerNumber, Transaction);

                if (LedgerTable.Rows.Count == 0)
                {
                    throw new Exception(String.Format(Catalog.GetString("Ledger {0} doesn't exist."), FLedgerNumber));
                }

                ImportMessage = Catalog.GetString("Parsing first line");
                AGiftRow previousGift = null;

                // Go round a loop reading the file line by line
                FImportLine = sr.ReadLine();

                while (FImportLine != null)
                {
                    RowNumber++;

                    // skip empty lines and commented lines
                    if ((FImportLine.Trim().Length > 0) && !FImportLine.StartsWith("/*") && !FImportLine.StartsWith("#"))
                    {
                        int numberOfElements = StringHelper.GetCSVList(FImportLine, FDelimiter).Count;

                        // Read the row analysisType - there is no 'validation' on this so we can make the call with null parameters
                        string RowType = ImportString(Catalog.GetString("row type"), null, null);

                        if (RowType == "B")
                        {
                            // It is a Batch row
                            if (numberOfElements != 9)
                            {
                                AMessages.Add(new TVerificationResult(String.Format(Catalog.GetString("Parsing error in Line {0}"), RowNumber),
                                        Catalog.GetString("Wrong number of batch columns.  The correct number is 9 columns."),
                                        TResultSeverity.Resv_Critical));

                                FImportLine = sr.ReadLine();
                                continue;
                            }

                            //Check if this is the start of a new batch (i.e. not the first batch)
                            if ((previousGift != null) && (giftBatch != null))
                            {
                                //New batch so set total amount of Batch for previous batch
                                giftBatch.BatchTotal = totalBatchAmount;

                                if (TVerificationHelper.IsNullOrOnlyNonCritical(AMessages))
                                {
                                    AGiftBatchAccess.SubmitChanges(FMainDS.AGiftBatch, Transaction);
                                    // no need to AcceptChanges() because we will create a new TDS below
                                }

                                // We use a new TDS for each new batch
                                FMainDS = new GiftBatchTDS();
                                previousGift = null;
                            }

                            totalBatchAmount = 0;

                            // Parse the complete line and validate it
                            ParseBatchLine(ref giftBatch, ref Transaction, ref LedgerTable, ref ImportMessage, RowNumber,
                                AMessages, ValidationControlsDictBatch, AccountTable, CostCentreTable);

                            if (TVerificationHelper.IsNullOrOnlyNonCritical(AMessages))
                            {
                                // This row passes validation so we can commit the change
                                ImportMessage = Catalog.GetString("Saving gift batch");

                                AGiftBatchAccess.SubmitChanges(FMainDS.AGiftBatch, Transaction);
                                FMainDS.AGiftBatch.AcceptChanges();
                            }

                            BatchDetailCounter = 0;
                            TProgressTracker.SetCurrentState(DomainManager.GClientID.ToString(),
                                string.Format(Catalog.GetString("Batch {0}"), giftBatch.BatchNumber),
                                10);
                        }
                        else if (RowType == "T")
                        {
                            // It is a Transaction row
                            if (numberOfElements < 12) // Perhaps this CSV file is a summary, and can't be imported?
                            {
                                AMessages.Add(new TVerificationResult(String.Format(Catalog.GetString("Parsing error in Line {0}"), RowNumber),
                                        Catalog.GetString("Wrong number of gift columns. (This may be a summary?)"), TResultSeverity.Resv_Critical));
                                FImportLine = sr.ReadLine();
                                continue;
                            }
                            else if ((numberOfElements != 21) && (numberOfElements != 27))
                            {
                                AMessages.Add(new TVerificationResult(String.Format(Catalog.GetString("Parsing error in Line {0}"), RowNumber),
                                        Catalog.GetString("Wrong number of gift columns. Expected either 21 or 27."), TResultSeverity.Resv_Critical));
                                FImportLine = sr.ReadLine();
                                continue;
                            }

                            if (giftBatch == null)
                            {
                                AMessages.Add(new TVerificationResult(String.Format(Catalog.GetString("Parsing error in Line {0}"), RowNumber),
                                        Catalog.GetString(
                                            "Expected a GiftBatch line, but found a Gift. Will create a dummy working batch for the current period."),
                                        TResultSeverity.Resv_Critical));

                                // in order to carry on we will make a dummy batch and force the date to fit
                                giftBatch = TGiftBatchFunctions.CreateANewGiftBatchRow(ref FMainDS,
                                    ref Transaction,
                                    ref LedgerTable,
                                    FLedgerNumber,
                                    DateTime.Today);
                            }

                            // Parse the line into a new row
                            AGiftRow gift = FMainDS.AGift.NewRowTyped(true);
                            ParseTransactionLine(gift, giftBatch, ref previousGift, numberOfElements, ref totalBatchAmount, ref ImportMessage,
                                RowNumber, AMessages, ValidationControlsDictGift, ValidationControlsDictGiftDetail);

                            if (TVerificationHelper.IsNullOrOnlyNonCritical(AMessages))
                            {
                                ImportMessage = Catalog.GetString("Saving gift");
                                AGiftAccess.SubmitChanges(FMainDS.AGift, Transaction);
                                FMainDS.AGift.AcceptChanges();

                                ImportMessage = Catalog.GetString("Saving giftdetails");
                                AGiftDetailAccess.SubmitChanges(FMainDS.AGiftDetail, Transaction);
                                FMainDS.AGiftDetail.AcceptChanges();
                            }

                            // Update progress tracker every 50 records
                            if (++BatchDetailCounter % 50 == 0)
                            {
                                TProgressTracker.SetCurrentState(DomainManager.GClientID.ToString(),
                                    string.Format(Catalog.GetString("Batch {0} - Importing gift detail"), giftBatch.BatchNumber),
                                    (BatchDetailCounter / 50 + 2) * 10 > 90 ? 90 : (BatchDetailCounter / 50 + 2) * 10);
                            }
                        } // If known row analysisType
                        else
                        {
                            AMessages.Add(new TVerificationResult(String.Format(Catalog.GetString("Parsing error in Line {0}"), RowNumber),
                                    Catalog.GetString("Invalid Row Type. Perhaps using wrong CSV separator?"), TResultSeverity.Resv_Critical));
                        }
                    }  // if the CSV line qualifies

                    if (AMessages.Count > 100)
                    {
                        // This probably means that it is a big file and the user has made the same mistake many times over
                        break;
                    }

                    // Read the next line
                    FImportLine = sr.ReadLine();
                }  // while CSV lines

                // Finished reading the file - did we have critical errors?
                if (!TVerificationHelper.IsNullOrOnlyNonCritical(AMessages))
                {
                    TProgressTracker.SetCurrentState(DomainManager.GClientID.ToString(),
                        Catalog.GetString("Batch has critical errors"),
                        0);

                    // Record error count
                    AMessages.Add(new TVerificationResult(Catalog.GetString("Import information"),
                            String.Format(Catalog.GetString("{0} messages reported."), AMessages.Count), TResultSeverity.Resv_Info));

                    if (FImportLine == null)
                    {
                        // We did reach the end of the file
                        AMessages.Add(new TVerificationResult(Catalog.GetString("Import information"),
                                Catalog.GetString(
                                    "Reached the end of file but errors occurred. When these errors are fixed the batch will import successfully."),
                                TResultSeverity.Resv_Info));
                    }
                    else
                    {
                        // We gave up before the end
                        AMessages.Add(new TVerificationResult(Catalog.GetString("Import information"),
                                Catalog.GetString(
                                    "Stopped reading the file after generating more than 100 messages.  The file may contian more errors beyond the ones listed here."),
                                TResultSeverity.Resv_Info));
                    }

                    TLogging.Log("Return from here!");

                            if (TaxDeductiblePercentageEnabled)
                            {
                                // Sets TaxDeductiblePct and uses it to calculate the tax deductibility amounts for a Gift Detail
                                TGift.SetDefaultTaxDeductibilityData(ref giftDetails, gift.DateEntered, FTransaction);
                            }

                    // Do the 'finally' actions and return false
                    return false;
                }

                // Everything is ok, so we can do our finish actions

                //Update batch total for the last batch entered.
                if (giftBatch != null)
                {
                    giftBatch.BatchTotal = totalBatchAmount;
                }

                ImportMessage = Catalog.GetString("Saving all data into the database");

                //Finally save pending changes (the last number is updated !)
                AGiftBatchAccess.SubmitChanges(FMainDS.AGiftBatch, Transaction);
                ALedgerAccess.SubmitChanges(LedgerTable, Transaction);
                FMainDS.AGiftBatch.AcceptChanges();
                FMainDS.ALedger.AcceptChanges();

                // Commit the transaction (we know that we got a new one and can control it)
                DBAccess.GDBAccessObj.CommitTransaction();
                ok = true;
            }
            catch (Exception ex)
            {
                // Parse the exception text for possible references to database foreign keys
                // Make the message more friendly in that case
                string friendlyExceptionText = MakeFriendlyFKExceptions(ex);

                if (AMessages == null)
                {
                    AMessages = new TVerificationResultCollection();
                }

                if (RowNumber > 0)
                {
                    // At least we made a start
                    string msg = ImportMessage;

                    if (friendlyExceptionText.Length > 0)
                    {
                        msg += FNewLine + friendlyExceptionText;
                    }

                    AMessages.Add(new TVerificationResult(String.Format(
                                Catalog.GetString("An exception occurred while parsing line {0}"), RowNumber), msg, TResultSeverity.Resv_Critical));
                }
                else
                {
                    // We got an exception before we even started parsing the rows (getting a transaction?)
                    AMessages.Add(new TVerificationResult(String.Format(
                                Catalog.GetString("An exception occurred while parsing line {0}"),
                                RowNumber), friendlyExceptionText, TResultSeverity.Resv_Critical));
                }

                TProgressTracker.SetCurrentState(DomainManager.GClientID.ToString(),
                    Catalog.GetString("Exception Occurred"),
                    0);

                ok = false;
            }
            finally
            {
                try
                {
                    sr.Close();
                }
                catch (Exception Exc)
                {
                    TLogging.Log("An Exception occured while closing the Import File:" + Environment.NewLine + Exc.ToString());

                    if (AMessages == null)
                    {
                        AMessages = new TVerificationResultCollection();
                    }

                    AMessages.Add(new TVerificationResult(Catalog.GetString("Import exception"),
                            Catalog.GetString("A problem was encountered while closing the Import File:"),
                            TResultSeverity.Resv_Critical));

                    TProgressTracker.SetCurrentState(DomainManager.GClientID.ToString(),
                        Catalog.GetString("Exception Occurred"),
                        0);

                    TProgressTracker.FinishJob(DomainManager.GClientID.ToString());

                    throw;
                }

                if (ok)
                {
                    TProgressTracker.SetCurrentState(DomainManager.GClientID.ToString(),
                        Catalog.GetString("Gift batch import successful"),
                        100);
                }
                else
                {
                    DBAccess.GDBAccessObj.RollbackTransaction();

                    if (AMessages == null)
                    {
                        AMessages = new TVerificationResultCollection();
                    }

                    AMessages.Add(new TVerificationResult(Catalog.GetString("Import Information"),
                            Catalog.GetString("None of the data from the import was saved."),
                            TResultSeverity.Resv_Critical));

                    TProgressTracker.SetCurrentState(DomainManager.GClientID.ToString(),
                        Catalog.GetString("Data could not be saved."),
                        0);
                }

                TProgressTracker.FinishJob(DomainManager.GClientID.ToString());
            } // end of 'finally'

            return ok;
        }

        private void ParseBatchLine(ref AGiftBatchRow AGiftBatch, ref TDBTransaction ATransaction, ref ALedgerTable ALedgerTable,
            ref string AImportMessage, int ARowNumber, TVerificationResultCollection AMessages, TValidationControlsDict AValidationControlsDictBatch,
            AAccountTable AValidationAccountTable, ACostCentreTable AValidationCostCentreTable)
        {
            // There are 8 elements to import
            string BatchDescription = ImportString(Catalog.GetString("Batch description"),
                FMainDS.AGiftBatch.ColumnBatchDescription, AValidationControlsDictBatch);
            string BankAccountCode = ImportString(Catalog.GetString("Bank account code"),
                FMainDS.AGiftBatch.ColumnBankAccountCode, AValidationControlsDictBatch);
            decimal HashTotal = ImportDecimal(Catalog.GetString("Hash total"),
                FMainDS.AGiftBatch.ColumnHashTotal, ARowNumber, AMessages, AValidationControlsDictBatch);
            DateTime GlEffectiveDate = ImportDate(Catalog.GetString("Effective Date"),
                FMainDS.AGiftBatch.ColumnGlEffectiveDate, ARowNumber, AMessages, AValidationControlsDictBatch);

            AImportMessage = "Creating new batch";

            // This call sets: BatchNumber, BatchYear, BatchPeriod, GlEffectiveDate, ExchangeRateToBase, BatchDescription, BankAccountCode
            //  BankCostCentre and CurrencyCode.  The effective date will NOT be modified.
            //  The first three are not validated because they should be ok by default
            AGiftBatch = TGiftBatchFunctions.CreateANewGiftBatchRow(ref FMainDS,
                ref ATransaction,
                ref ALedgerTable,
                FLedgerNumber,
                GlEffectiveDate,
                false);

            // Now we modify some of these in the light of the imported data
            AGiftBatch.BatchDescription = BatchDescription;
            AGiftBatch.BankAccountCode = BankAccountCode;
            AGiftBatch.HashTotal = HashTotal;
            AGiftBatch.CurrencyCode = ImportString(Catalog.GetString("Currency code"),
                FMainDS.AGiftBatch.ColumnCurrencyCode, AValidationControlsDictBatch);
            AGiftBatch.ExchangeRateToBase = ImportDecimal(Catalog.GetString("Exchange rate to base"),
                FMainDS.AGiftBatch.ColumnExchangeRateToBase, ARowNumber, AMessages, AValidationControlsDictBatch);

            AGiftBatch.BankCostCentre = ImportString(Catalog.GetString("Bank cost centre"),
                FMainDS.AGiftBatch.ColumnBankCostCentre, AValidationControlsDictBatch);
            AGiftBatch.GiftType = ImportString(Catalog.GetString("Gift type"),
                FMainDS.AGiftBatch.ColumnGiftType, AValidationControlsDictBatch, false);     // can be empty, will default to GIFT

            int messageCountBeforeValidate = AMessages.Count;

            // Do our standard gift batch validation checks on this row
            AImportMessage = Catalog.GetString("Validating the gift batch data");
            AGiftBatchValidation.Validate(this, AGiftBatch, ref AMessages, AValidationControlsDictBatch);

            // And do the additional manual ones
            AImportMessage = Catalog.GetString("Additional validation of the gift batch data");
            TSharedFinanceValidation_Gift.ValidateGiftBatchManual(this, AGiftBatch, ref AMessages,
                AValidationControlsDictBatch, AValidationAccountTable, AValidationCostCentreTable);

            for (int i = messageCountBeforeValidate; i < AMessages.Count; i++)
            {
                ((TVerificationResult)AMessages[i]).OverrideResultContext(String.Format("Validation error in line {0}", ARowNumber));

                if (AMessages[i] is TScreenVerificationResult)
                {
                    TVerificationResult downgrade = new TVerificationResult((TScreenVerificationResult)AMessages[i]);
                    AMessages.RemoveAt(i);
                    AMessages.Insert(i, downgrade);
                }
            }

            if (AGiftBatch.ExchangeRateToBase > 10000000)  // Huge numbers here indicate that the decimal comma/point is incorrect.
            {
                AMessages.Add(new TVerificationResult(String.Format(Catalog.GetString("Import validation error in Line {0}"), ARowNumber),
                        String.Format(Catalog.GetString("A huge exchange rate of {0} suggest decimal point format problem."),
                            AGiftBatch.ExchangeRateToBase),
                        TResultSeverity.Resv_Noncritical));
            }
        }

        private void ParseTransactionLine(AGiftRow AGift, AGiftBatchRow AGiftBatch, ref AGiftRow APreviousGift, int ANumberOfColumns,
            ref decimal ATotalBatchAmount, ref string AImportMessage, int ARowNumber, TVerificationResultCollection AMessages,
            TValidationControlsDict AValidationControlsDictGift, TValidationControlsDict AValidationControlsDictGiftDetail)
        {
            //this is the format with extra columns
            bool HasExtraColumns = (ANumberOfColumns >= 27);

            AImportMessage = Catalog.GetString("Importing the gift data");

            AGift.DonorKey = ImportInt64(Catalog.GetString("Donor key"),
                FMainDS.AGift.ColumnDonorKey, ARowNumber, AMessages, AValidationControlsDictGift);

            ImportString(Catalog.GetString("short name of donor (unused)"), null, null); // unused

            AGift.MethodOfGivingCode = ImportString(Catalog.GetString("Method of giving Code"),
                FMainDS.AGift.ColumnMethodOfGivingCode, AValidationControlsDictGift, false);
            AGift.MethodOfPaymentCode = ImportString(Catalog.GetString("Method Of Payment Code"),
                FMainDS.AGift.ColumnMethodOfPaymentCode, AValidationControlsDictGift, false);
            AGift.Reference = ImportString(Catalog.GetString("Reference"),
                FMainDS.AGift.ColumnReference, AValidationControlsDictGift, false);
            AGift.ReceiptLetterCode = ImportString(Catalog.GetString("Receipt letter code"),
                FMainDS.AGift.ColumnReceiptLetterCode, AValidationControlsDictGift, false);

            if (HasExtraColumns)
            {
                AGift.ReceiptNumber = ImportInt32(Catalog.GetString("Receipt number"),
                    FMainDS.AGift.ColumnReceiptNumber, ARowNumber, AMessages, AValidationControlsDictGift);
                AGift.FirstTimeGift = ImportBoolean(Catalog.GetString("First time gift"),
                    FMainDS.AGift.ColumnFirstTimeGift, AValidationControlsDictGift);
                AGift.ReceiptPrinted = ImportBoolean(Catalog.GetString("Receipt printed"),
                    FMainDS.AGift.ColumnReceiptPrinted, AValidationControlsDictGift);
            }

            AImportMessage = Catalog.GetString("Importing the gift details");

            AGiftDetailRow giftDetails = FMainDS.AGiftDetail.NewRowTyped(true);

            if ((APreviousGift != null) && (AGift.DonorKey == APreviousGift.DonorKey)
                && (AGift.MethodOfGivingCode == APreviousGift.MethodOfGivingCode)
                && (AGift.MethodOfPaymentCode == APreviousGift.MethodOfPaymentCode)
                && (AGift.Reference == APreviousGift.Reference)
                && (AGift.ReceiptLetterCode == APreviousGift.ReceiptLetterCode)
                && (AGift.ReceiptNumber == APreviousGift.ReceiptNumber)
                && (AGift.FirstTimeGift == APreviousGift.FirstTimeGift)
                && (AGift.ReceiptPrinted == APreviousGift.ReceiptPrinted))
            {
                // this row is a new detail for the previousGift
                AGift = APreviousGift;
                AGift.LastDetailNumber++;
                giftDetails.DetailNumber = AGift.LastDetailNumber;
            }
            else
            {
                APreviousGift = AGift;
                AGift.LedgerNumber = AGiftBatch.LedgerNumber;
                AGift.BatchNumber = AGiftBatch.BatchNumber;
                AGift.GiftTransactionNumber = AGiftBatch.LastGiftNumber + 1;
                AGiftBatch.LastGiftNumber++;
                AGift.LastDetailNumber = 1;
                FMainDS.AGift.Rows.Add(AGift);
                giftDetails.DetailNumber = 1;
            }

            giftDetails.LedgerNumber = AGift.LedgerNumber;
            giftDetails.BatchNumber = AGiftBatch.BatchNumber;
            giftDetails.GiftTransactionNumber = AGift.GiftTransactionNumber;
            FMainDS.AGiftDetail.Rows.Add(giftDetails);

            giftDetails.RecipientKey = ImportInt64(Catalog.GetString("Recipient key"),
                FMainDS.AGiftDetail.ColumnRecipientKey, ARowNumber, AMessages, AValidationControlsDictGiftDetail);

            ImportString(Catalog.GetString("short name of recipient (unused)"), null, null); // unused

            if (HasExtraColumns)
            {
                giftDetails.RecipientLedgerNumber = ImportInt32(Catalog.GetString("Recipient ledger number"),
                    FMainDS.AGiftDetail.ColumnRecipientLedgerNumber, ARowNumber, AMessages, AValidationControlsDictGiftDetail);
            }

            decimal currentGiftAmount = ImportDecimal(Catalog.GetString("Gift amount"),
                FMainDS.AGiftDetail.ColumnGiftTransactionAmount, ARowNumber, AMessages, AValidationControlsDictGiftDetail);
            giftDetails.GiftAmount = currentGiftAmount;
            giftDetails.GiftTransactionAmount = currentGiftAmount;
            ATotalBatchAmount += currentGiftAmount;
            // TODO: currency translation

            if (HasExtraColumns)
            {
                giftDetails.GiftAmountIntl = ImportDecimal(Catalog.GetString("Gift amount intl"),
                    FMainDS.AGiftDetail.ColumnGiftAmountIntl, ARowNumber, AMessages, AValidationControlsDictGiftDetail);
            }

            giftDetails.ConfidentialGiftFlag = ImportBoolean(Catalog.GetString("Confidential gift"),
                FMainDS.AGiftDetail.ColumnConfidentialGiftFlag, AValidationControlsDictGiftDetail);
            giftDetails.MotivationGroupCode = ImportString(Catalog.GetString("Motivation group code"),
                FMainDS.AGiftDetail.ColumnMotivationGroupCode, AValidationControlsDictGiftDetail);
            giftDetails.MotivationDetailCode = ImportString(Catalog.GetString("Motivation detail"),
                FMainDS.AGiftDetail.ColumnMotivationDetailCode, AValidationControlsDictGiftDetail);

            if (HasExtraColumns)
            {
                giftDetails.CostCentreCode = ImportString(Catalog.GetString("Cost centre code"),
                    FMainDS.AGiftDetail.ColumnCostCentreCode, AValidationControlsDictGiftDetail);
            }
            else
            {
                // "In Petra Cost Centre is always inferred from recipient field and motivation detail so is not needed in the import."
                giftDetails.CostCentreCode = InferCostCentre(giftDetails);
            }

            giftDetails.GiftCommentOne = ImportString(Catalog.GetString("Gift comment one"),
                FMainDS.AGiftDetail.ColumnGiftCommentOne, AValidationControlsDictGiftDetail, false);
            giftDetails.CommentOneType = ImportString(Catalog.GetString("Comment one type"),
                FMainDS.AGiftDetail.ColumnCommentOneType, AValidationControlsDictGiftDetail, false);

            giftDetails.MailingCode = ImportString(Catalog.GetString("Mailing code"),
                FMainDS.AGiftDetail.ColumnMailingCode, AValidationControlsDictGiftDetail);

            giftDetails.GiftCommentTwo = ImportString(Catalog.GetString("Gift comment two"),
                FMainDS.AGiftDetail.ColumnGiftCommentTwo, AValidationControlsDictGiftDetail, false);
            giftDetails.CommentTwoType = ImportString(Catalog.GetString("Comment two type"),
                FMainDS.AGiftDetail.ColumnCommentTwoType, AValidationControlsDictGiftDetail, false);
            giftDetails.GiftCommentThree = ImportString(Catalog.GetString("Gift comment three"),
                FMainDS.AGiftDetail.ColumnGiftCommentThree, AValidationControlsDictGiftDetail, false);
            giftDetails.CommentThreeType = ImportString(Catalog.GetString("Comment three type"),
                FMainDS.AGiftDetail.ColumnCommentThreeType, AValidationControlsDictGiftDetail, false);
            giftDetails.TaxDeductible = ImportBoolean(Catalog.GetString("Tax deductible"),
                FMainDS.AGiftDetail.ColumnTaxDeductible, AValidationControlsDictGiftDetail);

            AGift.DateEntered = AGiftBatch.GlEffectiveDate;

            AImportMessage = Catalog.GetString("Validating the gift data");

            int messageCountBeforeValidate = AMessages.Count;

            // Do our standard validation on this gift
            AGiftValidation.Validate(this, AGift, ref AMessages, AValidationControlsDictGift);
            TSharedFinanceValidation_Gift.ValidateGiftManual(this, AGift, AGiftBatch.BatchYear, AGiftBatch.BatchPeriod,
                null, ref AMessages, AValidationControlsDictGift);

            AImportMessage = Catalog.GetString("Validating the gift details data");

            AGiftDetailValidation.Validate(this, giftDetails, ref AMessages, AValidationControlsDictGiftDetail);
            TSharedFinanceValidation_Gift.ValidateGiftDetailManual(this, (GiftBatchTDSAGiftDetailRow)giftDetails,
                ref AMessages, AValidationControlsDictGiftDetail, giftDetails.RecipientKey);

            for (int i = messageCountBeforeValidate; i < AMessages.Count; i++)
            {
                ((TVerificationResult)AMessages[i]).OverrideResultContext(String.Format("Validation error in line {0}", ARowNumber));

                if (AMessages[i] is TScreenVerificationResult)
                {
                    TVerificationResult downgrade = new TVerificationResult((TScreenVerificationResult)AMessages[i]);
                    AMessages.RemoveAt(i);
                    AMessages.Insert(i, downgrade);
                }
            }
        }

        /// <summary>
        /// returns the most recently imported gift batch
        /// </summary>
        public Int32 GetLastGiftBatchNumber()
        {
            if ((FMainDS != null) && (FMainDS.AGiftBatch != null) && (FMainDS.AGiftBatch.Count > 0))
            {
                return FMainDS.AGiftBatch[FMainDS.AGiftBatch.Count - 1].BatchNumber;
            }

            return -1;
        }

        private String MakeFriendlyFKExceptions(Exception ex)
        {
            //note that this is only done for "user errors" not for program errors!
            String innerMessage = ex.InnerException.ToString();

            string formatStr = Catalog.GetString("  Do you need to add this code to the '{0}' main setup screen?");
            string createNewPartner = Catalog.GetString("  Do you need to add this partner using the 'Create Partner' main setup screen?");

            if (innerMessage.Contains("a_gift_batch_fk2"))
            {
                return Catalog.GetString("Unknown account code.") + String.Format(formatStr, "Manage Accounts");
            }

            if (innerMessage.Contains("a_gift_batch_fk3"))
            {
                return Catalog.GetString("Unknown cost centre.") + String.Format(formatStr, "Manage Cost Centres");
            }

            if (innerMessage.Contains("a_gift_batch_fk4"))
            {
                return Catalog.GetString("Unknown currency code.") + String.Format(formatStr, "Currencies");
            }

            if (innerMessage.Contains("a_gift_fk2"))
            {
                return Catalog.GetString("Unknown method of giving.") + String.Format(formatStr, "Methods Of Giving");
            }

            if (innerMessage.Contains("a_gift_fk3"))
            {
                return Catalog.GetString("Unknown method of payment.") + String.Format(formatStr, "Methods Of Payment");
            }

            if (innerMessage.Contains("a_gift_fk4"))
            {
                return Catalog.GetString("Unknown donor partner key.") + createNewPartner;
            }

            if (innerMessage.Contains("a_gift_detail_fk2"))
            {
                return Catalog.GetString("Unknown motivation detail.") + String.Format(formatStr, "Motivation Details");
            }

            if (innerMessage.Contains("a_gift_detail_fk3"))
            {
                return Catalog.GetString("Unknown recipient partner key.") + createNewPartner;
            }

            if (innerMessage.Contains("a_gift_detail_fk4"))
            {
                return Catalog.GetString("Unknown mailing code.") + String.Format(formatStr, "Mailings");
            }

            if (innerMessage.Contains("a_gift_detail_fk5"))
            {
                return Catalog.GetString("Unknown recipient ledger number.") + createNewPartner;
            }

            if (innerMessage.Contains("a_gift_detail_fk6"))
            {
                return Catalog.GetString("Unknown cost centre.") + String.Format(formatStr, "Manage Cost Centres");
            }

            TLogging.Log("Importing Gift batch: " + ex.ToString());

            return ex.Message;
        }

        private String ImportString(String AColumnTitle,
            DataColumn ADataColumn,
            TValidationControlsDict AValidationColumnsDict,
            bool ATreatEmptyStringAsText = true)
        {
            if ((ADataColumn != null) && (AValidationColumnsDict != null) && !AValidationColumnsDict.ContainsKey(ADataColumn))
            {
                AValidationColumnsDict.Add(ADataColumn, new TValidationControlsData(null, AColumnTitle));
            }

            String sReturn = StringHelper.GetNextCSV(ref FImportLine, FDelimiter);

            if ((sReturn.Length == 0) && !ATreatEmptyStringAsText)
            {
                return null;
            }

            return sReturn;
        }

        private Boolean ImportBoolean(String AColumnTitle, DataColumn ADataColumn, TValidationControlsDict AValidationColumnsDict)
        {
            if ((ADataColumn != null) && (AValidationColumnsDict != null) && !AValidationColumnsDict.ContainsKey(ADataColumn))
            {
                AValidationColumnsDict.Add(ADataColumn, new TValidationControlsData(null, AColumnTitle));
            }

            String sReturn = StringHelper.GetNextCSV(ref FImportLine, FDelimiter);
            return sReturn.ToLower().Equals("yes");
        }

        private Int64 ImportInt64(String AColumnTitle,
            DataColumn ADataColumn,
            int ARowNumber,
            TVerificationResultCollection AMessages,
            TValidationControlsDict AValidationColumnsDict)
        {
            if ((ADataColumn != null) && (AValidationColumnsDict != null) && !AValidationColumnsDict.ContainsKey(ADataColumn))
            {
                AValidationColumnsDict.Add(ADataColumn, new TValidationControlsData(null, AColumnTitle));
            }

            String sReturn = StringHelper.GetNextCSV(ref FImportLine, FDelimiter);
            Int64 retVal;

            if (Int64.TryParse(sReturn, out retVal))
            {
                return retVal;
            }

            AMessages.Add(new TVerificationResult(String.Format(Catalog.GetString("Parsing error in line {0} - column '{1}'"), ARowNumber,
                        AColumnTitle),
                    String.Format(Catalog.GetString("Cannot convert '{0}' to a number. Will assume a value of -1."), sReturn),
                    TResultSeverity.Resv_Critical));
            return -1;
        }

        private Int32 ImportInt32(String AColumnTitle,
            DataColumn ADataColumn,
            int ARowNumber,
            TVerificationResultCollection AMessages,
            TValidationControlsDict AValidationColumnsDict)
        {
            if ((ADataColumn != null) && (AValidationColumnsDict != null) && !AValidationColumnsDict.ContainsKey(ADataColumn))
            {
                AValidationColumnsDict.Add(ADataColumn, new TValidationControlsData(null, AColumnTitle));
            }

            String sReturn = StringHelper.GetNextCSV(ref FImportLine, FDelimiter);
            Int32 retVal;

            if (Int32.TryParse(sReturn, out retVal))
            {
                return retVal;
            }

            AMessages.Add(new TVerificationResult(String.Format(Catalog.GetString("Parsing error in line {0} - column '{1}'"), ARowNumber,
                        AColumnTitle),
                    String.Format(Catalog.GetString("Cannot convert '{0}' to a number. Will assume a value of -1."), sReturn),
                    TResultSeverity.Resv_Critical));
            return -1;
        }

        private decimal ImportDecimal(String AColumnTitle,
            DataColumn ADataColumn,
            int ARowNumber,
            TVerificationResultCollection AMessages,
            TValidationControlsDict AValidationColumnsDict)
        {
            if ((ADataColumn != null) && (AValidationColumnsDict != null) && !AValidationColumnsDict.ContainsKey(ADataColumn))
            {
                AValidationColumnsDict.Add(ADataColumn, new TValidationControlsData(null, AColumnTitle));
            }

            String sReturn = StringHelper.GetNextCSV(ref FImportLine, FDelimiter);
            try
            {
                decimal dec = Convert.ToDecimal(sReturn, FCultureInfoNumberFormat);
                return dec;
            }
            catch
            {
                AMessages.Add(new TVerificationResult(String.Format(Catalog.GetString("Parsing error in line {0} - column '{1}'"), ARowNumber,
                            AColumnTitle),
                        String.Format(Catalog.GetString("Cannot convert '{0}' to a decimal number. Will assume a value of 1.00."), sReturn),
                        TResultSeverity.Resv_Critical));
                return 1.0m;
            }
        }

        private DateTime ImportDate(String AColumnTitle,
            DataColumn ADataColumn,
            int ARowNumber,
            TVerificationResultCollection AMessages,
            TValidationControlsDict AValidationColumnsDict)
        {
            if ((ADataColumn != null) && (AValidationColumnsDict != null) && !AValidationColumnsDict.ContainsKey(ADataColumn))
            {
                AValidationColumnsDict.Add(ADataColumn, new TValidationControlsData(null, AColumnTitle));
            }

            String sDate = StringHelper.GetNextCSV(ref FImportLine, FDelimiter);
            DateTime dtReturn;

            try
            {
                dtReturn = Convert.ToDateTime(sDate, FCultureInfoDate);
            }
            catch (Exception)
            {
                AMessages.Add(new TVerificationResult(String.Format(Catalog.GetString("Parsing error in line {0} - column '{1}'"), ARowNumber,
                            AColumnTitle),
                        String.Format(Catalog.GetString("Cannot convert '{0}' to a date. Will assume a value of 'Today'."), sDate),
                        TResultSeverity.Resv_Critical));
                TLogging.Log("Problem parsing " + sDate + " with format " + FCultureInfoDate.DateTimeFormat.ShortDatePattern);
                return DateTime.Today;
            }

            return dtReturn;
        }
    }
}