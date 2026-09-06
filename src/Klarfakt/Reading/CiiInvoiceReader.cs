using System.Xml.Linq;
using Klarfakt.Model;

namespace Klarfakt.Reading;

internal sealed class CiiInvoiceReader
{
    private readonly ReadContext _context = new();
    private readonly XElement _root;

    internal CiiInvoiceReader(XElement root) => _root = root;

    internal ReadResult Read()
    {
        var invoice = new EInvoice();

        var context = _root.El(Ns.Rsm + "ExchangedDocumentContext");
        var document = _root.El(Ns.Rsm + "ExchangedDocument");
        var transaction = _root.El(Ns.Rsm + "SupplyChainTradeTransaction");
        var agreement = transaction.El(Ns.Ram + "ApplicableHeaderTradeAgreement");
        var delivery = transaction.El(Ns.Ram + "ApplicableHeaderTradeDelivery");
        var settlement = transaction.El(Ns.Ram + "ApplicableHeaderTradeSettlement");

        ReadContextAndDocument(invoice, context, document);
        ReadAgreement(invoice, agreement);
        ReadDelivery(invoice, delivery);
        ReadSettlement(invoice, settlement);
        ReadLines(invoice, transaction);

        return new ReadResult(invoice, _context.Findings);
    }

    private void ReadContextAndDocument(EInvoice invoice, XElement? context, XElement? document)
    {
        invoice.BusinessProcess = context
            .Descend(Ns.Ram + "BusinessProcessSpecifiedDocumentContextParameter", Ns.Ram + "ID").Text();
        invoice.SpecificationIdentifier = context
            .Descend(Ns.Ram + "GuidelineSpecifiedDocumentContextParameter", Ns.Ram + "ID").Text();

        invoice.Id = document.El(Ns.Ram + "ID").Text();
        invoice.TypeCode = document.El(Ns.Ram + "TypeCode").Text();
        invoice.IssueDate = _context.CiiDate(document.El(Ns.Ram + "IssueDateTime"));

        foreach (var note in document.Els(Ns.Ram + "IncludedNote"))
        {
            invoice.Notes.Add(new InvoiceNote
            {
                Text = note.El(Ns.Ram + "Content").Text(),
                SubjectCode = note.El(Ns.Ram + "SubjectCode").Text(),
            });
        }
    }

    private void ReadAgreement(EInvoice invoice, XElement? agreement)
    {
        invoice.BuyerReference = agreement.El(Ns.Ram + "BuyerReference").Text();
        invoice.Seller = ReadParty(agreement.El(Ns.Ram + "SellerTradeParty")) ?? new Party();
        invoice.Buyer = ReadParty(agreement.El(Ns.Ram + "BuyerTradeParty")) ?? new Party();
        invoice.SellerTaxRepresentative = ReadParty(agreement.El(Ns.Ram + "SellerTaxRepresentativeTradeParty"));

        invoice.PurchaseOrderReference = agreement
            .Descend(Ns.Ram + "BuyerOrderReferencedDocument", Ns.Ram + "IssuerAssignedID").Text();
        invoice.SalesOrderReference = agreement
            .Descend(Ns.Ram + "SellerOrderReferencedDocument", Ns.Ram + "IssuerAssignedID").Text();
        invoice.ContractReference = agreement
            .Descend(Ns.Ram + "ContractReferencedDocument", Ns.Ram + "IssuerAssignedID").Text();
        invoice.ProjectReference = agreement
            .Descend(Ns.Ram + "SpecifiedProcuringProject", Ns.Ram + "ID").Text();

        foreach (var reference in agreement.Els(Ns.Ram + "AdditionalReferencedDocument"))
        {
            var typeCode = reference.El(Ns.Ram + "TypeCode").Text();
            if (typeCode == "50")
            {
                invoice.TenderReference ??= reference.El(Ns.Ram + "IssuerAssignedID").Text();
                continue;
            }

            var binary = reference.El(Ns.Ram + "AttachmentBinaryObject");
            invoice.Attachments.Add(new Attachment
            {
                DocumentIdentifier = reference.El(Ns.Ram + "IssuerAssignedID").Text(),
                Description = reference.El(Ns.Ram + "Name").Text(),
                ExternalUri = reference.El(Ns.Ram + "URIID").Text(),
                FileName = binary.Attr("filename"),
                MimeCode = binary.Attr("mimeCode"),
                Content = _context.Binary(binary),
            });
        }
    }

    private void ReadDelivery(EInvoice invoice, XElement? element)
    {
        if (element is null) return;

        var shipTo = element.El(Ns.Ram + "ShipToTradeParty");
        var deliveryDate = _context.CiiDate(
            element.Descend(Ns.Ram + "ActualDeliverySupplyChainEvent", Ns.Ram + "OccurrenceDateTime"));

        if (shipTo is not null || deliveryDate is not null)
        {
            var delivery = new Delivery
            {
                Name = shipTo.El(Ns.Ram + "Name").Text(),
                ActualDeliveryDate = deliveryDate,
                Address = ReadAddress(shipTo.El(Ns.Ram + "PostalTradeAddress")),
            };

            var locationId = shipTo.El(Ns.Ram + "ID");
            if (locationId.Text() is { } location)
            {
                delivery.LocationIdentifier = new Identifier(location, locationId.Attr("schemeID"));
            }

            invoice.Delivery = delivery;
        }

        invoice.DespatchAdviceReference = element
            .Descend(Ns.Ram + "DespatchAdviceReferencedDocument", Ns.Ram + "IssuerAssignedID").Text();
        invoice.ReceivingAdviceReference = element
            .Descend(Ns.Ram + "ReceivingAdviceReferencedDocument", Ns.Ram + "IssuerAssignedID").Text();
    }

    private void ReadSettlement(EInvoice invoice, XElement? settlement)
    {
        if (settlement is null) return;

        invoice.CurrencyCode = settlement.El(Ns.Ram + "InvoiceCurrencyCode").Text();
        invoice.TaxCurrencyCode = settlement.El(Ns.Ram + "TaxCurrencyCode").Text();
        invoice.AccountingCost = settlement
            .Descend(Ns.Ram + "ReceivableSpecifiedTradeAccountingAccount", Ns.Ram + "ID").Text();
        invoice.Payee = ReadParty(settlement.El(Ns.Ram + "PayeeTradeParty"));
        invoice.InvoicePeriod = ReadPeriod(settlement.El(Ns.Ram + "BillingSpecifiedPeriod"));

        var payment = invoice.Payment;
        payment.RemittanceInformation = settlement.El(Ns.Ram + "PaymentReference").Text();

        foreach (var means in settlement.Els(Ns.Ram + "SpecifiedTradeSettlementPaymentMeans"))
        {
            payment.MeansCode ??= means.El(Ns.Ram + "TypeCode").Text();
            payment.MeansText ??= means.El(Ns.Ram + "Information").Text();

            var creditorAccount = means.El(Ns.Ram + "PayeePartyCreditorFinancialAccount");
            if (creditorAccount is not null)
            {
                payment.CreditTransfers.Add(new CreditTransfer
                {
                    AccountIdentifier = creditorAccount.El(Ns.Ram + "IBANID").Text()
                        ?? creditorAccount.El(Ns.Ram + "ProprietaryID").Text(),
                    AccountName = creditorAccount.El(Ns.Ram + "AccountName").Text(),
                    ServiceProviderIdentifier = means
                        .Descend(Ns.Ram + "PayeeSpecifiedCreditorFinancialInstitution", Ns.Ram + "BICID").Text(),
                });
            }

            var card = means.El(Ns.Ram + "ApplicableTradeSettlementFinancialCard");
            if (card is not null)
            {
                payment.Card ??= new PaymentCard
                {
                    PrimaryAccountNumber = card.El(Ns.Ram + "ID").Text(),
                    HolderName = card.El(Ns.Ram + "CardholderName").Text(),
                };
            }

            var debtorAccount = means
                .Descend(Ns.Ram + "PayerPartyDebtorFinancialAccount", Ns.Ram + "IBANID").Text();
            if (debtorAccount is not null)
            {
                payment.DirectDebit ??= new DirectDebit();
                payment.DirectDebit.DebitedAccountIdentifier ??= debtorAccount;
            }
        }

        foreach (var tax in settlement.Els(Ns.Ram + "ApplicableTradeTax"))
        {
            invoice.TaxBreakdown.Add(new TaxBreakdownEntry
            {
                TaxableAmount = _context.Decimal(tax.El(Ns.Ram + "BasisAmount")),
                TaxAmount = _context.Decimal(tax.El(Ns.Ram + "CalculatedAmount")),
                CategoryCode = tax.El(Ns.Ram + "CategoryCode").Text(),
                Percent = _context.Decimal(tax.El(Ns.Ram + "RateApplicablePercent")),
                ExemptionReason = tax.El(Ns.Ram + "ExemptionReason").Text(),
                ExemptionReasonCode = tax.El(Ns.Ram + "ExemptionReasonCode").Text(),
                TaxPointDate = _context.CiiDate(tax.El(Ns.Ram + "TaxPointDate")),
                DueDateTypeCode = tax.El(Ns.Ram + "DueDateTypeCode").Text(),
            });
        }

        invoice.TaxPointDate = invoice.TaxBreakdown.FirstOrDefault(entry => entry.TaxPointDate is not null)?.TaxPointDate;

        foreach (var element in settlement.Els(Ns.Ram + "SpecifiedTradeAllowanceCharge"))
        {
            invoice.AllowancesAndCharges.Add(ReadAllowanceCharge(element));
        }

        foreach (var terms in settlement.Els(Ns.Ram + "SpecifiedTradePaymentTerms"))
        {
            payment.Terms ??= terms.El(Ns.Ram + "Description").Text();
            invoice.DueDate ??= _context.CiiDate(terms.El(Ns.Ram + "DueDateDateTime"));

            var mandate = terms.El(Ns.Ram + "DirectDebitMandateID").Text();
            if (mandate is not null)
            {
                payment.DirectDebit ??= new DirectDebit();
                payment.DirectDebit.MandateReference ??= mandate;
            }
        }

        foreach (var reference in settlement.Els(Ns.Ram + "InvoiceReferencedDocument"))
        {
            invoice.PrecedingInvoices.Add(new PrecedingInvoiceReference
            {
                Id = reference.El(Ns.Ram + "IssuerAssignedID").Text(),
                IssueDate = _context.CiiDate(reference.El(Ns.Ram + "FormattedIssueDateTime")),
            });
        }

        ReadTotals(invoice, settlement.El(Ns.Ram + "SpecifiedTradeSettlementHeaderMonetarySummation"));
    }

    private void ReadTotals(EInvoice invoice, XElement? element)
    {
        if (element is null) return;

        var totals = invoice.Totals;
        totals.LineExtensionAmount = _context.Decimal(element.El(Ns.Ram + "LineTotalAmount"));
        totals.AllowanceTotalAmount = _context.Decimal(element.El(Ns.Ram + "AllowanceTotalAmount"));
        totals.ChargeTotalAmount = _context.Decimal(element.El(Ns.Ram + "ChargeTotalAmount"));
        totals.TaxExclusiveAmount = _context.Decimal(element.El(Ns.Ram + "TaxBasisTotalAmount"));
        totals.TaxInclusiveAmount = _context.Decimal(element.El(Ns.Ram + "GrandTotalAmount"));
        totals.PrepaidAmount = _context.Decimal(element.El(Ns.Ram + "TotalPrepaidAmount"));
        totals.RoundingAmount = _context.Decimal(element.El(Ns.Ram + "RoundingAmount"));
        totals.DuePayableAmount = _context.Decimal(element.El(Ns.Ram + "DuePayableAmount"));

        foreach (var taxTotal in element.Els(Ns.Ram + "TaxTotalAmount"))
        {
            var currency = taxTotal.Attr("currencyID");
            if (currency is not null && invoice.TaxCurrencyCode is not null &&
                string.Equals(currency, invoice.TaxCurrencyCode, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(currency, invoice.CurrencyCode, StringComparison.OrdinalIgnoreCase))
            {
                totals.TaxTotalAmountInAccountingCurrency ??= _context.Decimal(taxTotal);
                continue;
            }

            totals.TaxTotalAmount ??= _context.Decimal(taxTotal);
        }
    }

    private void ReadLines(EInvoice invoice, XElement? transaction)
    {
        foreach (var element in transaction.Els(Ns.Ram + "IncludedSupplyChainTradeLineItem"))
        {
            var lineDocument = element.El(Ns.Ram + "AssociatedDocumentLineDocument");
            var lineAgreement = element.El(Ns.Ram + "SpecifiedLineTradeAgreement");
            var lineDelivery = element.El(Ns.Ram + "SpecifiedLineTradeDelivery");
            var lineSettlement = element.El(Ns.Ram + "SpecifiedLineTradeSettlement");
            var quantity = lineDelivery.El(Ns.Ram + "BilledQuantity");

            var line = new InvoiceLine
            {
                Id = lineDocument.El(Ns.Ram + "LineID").Text(),
                Note = lineDocument.Descend(Ns.Ram + "IncludedNote", Ns.Ram + "Content").Text(),
                Quantity = _context.Decimal(quantity),
                QuantityUnitCode = quantity.Attr("unitCode"),
                NetAmount = _context.Decimal(lineSettlement.Descend(
                    Ns.Ram + "SpecifiedTradeSettlementLineMonetarySummation", Ns.Ram + "LineTotalAmount")),
                OrderLineReference = lineAgreement
                    .Descend(Ns.Ram + "BuyerOrderReferencedDocument", Ns.Ram + "LineID").Text(),
                AccountingCost = lineSettlement
                    .Descend(Ns.Ram + "ReceivableSpecifiedTradeAccountingAccount", Ns.Ram + "ID").Text(),
                ObjectIdentifier = lineSettlement
                    .Descend(Ns.Ram + "AdditionalReferencedDocument", Ns.Ram + "IssuerAssignedID").Text(),
                Period = ReadPeriod(lineSettlement.El(Ns.Ram + "BillingSpecifiedPeriod")),
            };

            var tax = lineSettlement.El(Ns.Ram + "ApplicableTradeTax");
            line.TaxCategoryCode = tax.El(Ns.Ram + "CategoryCode").Text();
            line.TaxPercent = _context.Decimal(tax.El(Ns.Ram + "RateApplicablePercent"));

            foreach (var allowanceCharge in lineSettlement.Els(Ns.Ram + "SpecifiedTradeAllowanceCharge"))
            {
                line.AllowancesAndCharges.Add(ReadAllowanceCharge(allowanceCharge));
            }

            ReadPrice(lineAgreement, line.Price);
            ReadItem(element.El(Ns.Ram + "SpecifiedTradeProduct"), line.Item);

            invoice.Lines.Add(line);
        }
    }

    private void ReadPrice(XElement? agreement, LinePrice price)
    {
        if (agreement is null) return;

        var net = agreement.El(Ns.Ram + "NetPriceProductTradePrice");
        var gross = agreement.El(Ns.Ram + "GrossPriceProductTradePrice");
        var baseQuantity = net.El(Ns.Ram + "BasisQuantity") ?? gross.El(Ns.Ram + "BasisQuantity");

        price.NetAmount = _context.Decimal(net.El(Ns.Ram + "ChargeAmount"));
        price.GrossAmount = _context.Decimal(gross.El(Ns.Ram + "ChargeAmount"));
        price.Discount = _context.Decimal(gross
            .Descend(Ns.Ram + "AppliedTradeAllowanceCharge", Ns.Ram + "ActualAmount"));
        price.BaseQuantity = _context.Decimal(baseQuantity);
        price.BaseQuantityUnitCode = baseQuantity.Attr("unitCode");
    }

    private void ReadItem(XElement? element, LineItem item)
    {
        if (element is null) return;

        item.Name = element.El(Ns.Ram + "Name").Text();
        item.Description = element.El(Ns.Ram + "Description").Text();
        item.SellerIdentifier = element.El(Ns.Ram + "SellerAssignedID").Text();
        item.BuyerIdentifier = element.El(Ns.Ram + "BuyerAssignedID").Text();
        item.OriginCountryCode = element.Descend(Ns.Ram + "OriginTradeCountry", Ns.Ram + "ID").Text();

        var globalId = element.El(Ns.Ram + "GlobalID");
        if (globalId.Text() is { } global)
        {
            item.StandardIdentifier = new Identifier(global, globalId.Attr("schemeID"));
        }

        foreach (var classification in element.Els(Ns.Ram + "DesignatedProductClassification"))
        {
            var code = classification.El(Ns.Ram + "ClassCode");
            if (code.Text() is { } codeValue)
            {
                item.ClassificationIdentifiers.Add(new Identifier(codeValue, code.Attr("listID")));
            }
        }

        foreach (var characteristic in element.Els(Ns.Ram + "ApplicableProductCharacteristic"))
        {
            item.Attributes.Add(new ItemAttribute
            {
                Name = characteristic.El(Ns.Ram + "Description").Text(),
                Value = characteristic.El(Ns.Ram + "Value").Text(),
            });
        }
    }

    private AllowanceCharge ReadAllowanceCharge(XElement element)
    {
        var isCharge = _context.Indicator(
            element.Descend(Ns.Ram + "ChargeIndicator", Ns.Udt + "Indicator")) ?? false;

        return new AllowanceCharge
        {
            IsCharge = isCharge,
            Amount = _context.Decimal(element.El(Ns.Ram + "ActualAmount")),
            BaseAmount = _context.Decimal(element.El(Ns.Ram + "BasisAmount")),
            Percentage = _context.Decimal(element.El(Ns.Ram + "CalculationPercent")),
            Reason = element.El(Ns.Ram + "Reason").Text(),
            ReasonCode = element.El(Ns.Ram + "ReasonCode").Text(),
            TaxCategoryCode = element.Descend(Ns.Ram + "CategoryTradeTax", Ns.Ram + "CategoryCode").Text(),
            TaxPercent = _context.Decimal(element.Descend(Ns.Ram + "CategoryTradeTax", Ns.Ram + "RateApplicablePercent")),
        };
    }

    private Party? ReadParty(XElement? element)
    {
        if (element is null) return null;

        var legalOrganization = element.El(Ns.Ram + "SpecifiedLegalOrganization");
        var party = new Party
        {
            Name = element.El(Ns.Ram + "Name").Text(),
            TradingName = legalOrganization.El(Ns.Ram + "TradingBusinessName").Text(),
            AdditionalLegalInformation = element.El(Ns.Ram + "Description").Text(),
            Address = ReadAddress(element.El(Ns.Ram + "PostalTradeAddress")),
        };

        var legalId = legalOrganization.El(Ns.Ram + "ID");
        if (legalId.Text() is { } legal)
        {
            party.LegalRegistrationId = new Identifier(legal, legalId.Attr("schemeID"));
        }

        var endpoint = element.Descend(Ns.Ram + "URIUniversalCommunication", Ns.Ram + "URIID");
        if (endpoint.Text() is { } endpointValue)
        {
            party.ElectronicAddress = new Identifier(endpointValue, endpoint.Attr("schemeID"));
        }

        foreach (var id in element.Els(Ns.Ram + "ID"))
        {
            if (id.Text() is { } idValue)
            {
                party.Identifiers.Add(new Identifier(idValue, id.Attr("schemeID")));
            }
        }

        foreach (var globalId in element.Els(Ns.Ram + "GlobalID"))
        {
            if (globalId.Text() is { } idValue)
            {
                party.Identifiers.Add(new Identifier(idValue, globalId.Attr("schemeID")));
            }
        }

        foreach (var registration in element.Els(Ns.Ram + "SpecifiedTaxRegistration"))
        {
            var id = registration.El(Ns.Ram + "ID");
            if (id.Text() is not { } value) continue;

            var scheme = id.Attr("schemeID");
            if (string.Equals(scheme, "VA", StringComparison.OrdinalIgnoreCase))
            {
                party.VatIdentifier ??= value;
            }
            else
            {
                party.TaxRegistrationId ??= value;
            }
        }

        var contact = element.El(Ns.Ram + "DefinedTradeContact");
        if (contact is not null)
        {
            party.Contact = new Contact
            {
                Name = contact.El(Ns.Ram + "PersonName").Text(),
                Department = contact.El(Ns.Ram + "DepartmentName").Text(),
                Phone = contact.Descend(Ns.Ram + "TelephoneUniversalCommunication", Ns.Ram + "CompleteNumber").Text(),
                Email = contact.Descend(Ns.Ram + "EmailURIUniversalCommunication", Ns.Ram + "URIID").Text(),
            };
        }

        return party;
    }

    private static PostalAddress? ReadAddress(XElement? element)
    {
        if (element is null) return null;

        return new PostalAddress
        {
            Line1 = element.El(Ns.Ram + "LineOne").Text(),
            Line2 = element.El(Ns.Ram + "LineTwo").Text(),
            Line3 = element.El(Ns.Ram + "LineThree").Text(),
            City = element.El(Ns.Ram + "CityName").Text(),
            PostalCode = element.El(Ns.Ram + "PostcodeCode").Text(),
            CountrySubdivision = element.El(Ns.Ram + "CountrySubDivisionName").Text(),
            CountryCode = element.El(Ns.Ram + "CountryID").Text(),
        };
    }

    private DatePeriod? ReadPeriod(XElement? element)
    {
        if (element is null) return null;

        return new DatePeriod
        {
            StartDate = _context.CiiDate(element.El(Ns.Ram + "StartDateTime")),
            EndDate = _context.CiiDate(element.El(Ns.Ram + "EndDateTime")),
        };
    }
}
