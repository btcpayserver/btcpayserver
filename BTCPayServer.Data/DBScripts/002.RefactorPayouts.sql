-- Rename column
ALTER TABLE "Payouts" RENAME COLUMN "PaymentMethodId" TO "PayoutMethodId";

-- Add Currency column, guessed from the PaymentMethodId
ALTER TABLE "Payouts" ADD COLUMN "Currency" TEXT;
UPDATE "Payouts" SET
    "Currency" = split_part("PayoutMethodId", '_', 1),
    "PayoutMethodId"=
        CASE
            WHEN ("Blob"->>'Amount')::NUMERIC < 0 THEN 'TOPUP'
            WHEN split_part("PayoutMethodId", '_', 2) = 'LightningLike' THEN split_part("PayoutMethodId", '_', 1) || '-LN'
            ELSE split_part("PayoutMethodId", '_', 1) || '-CHAIN'
        END;
ALTER TABLE "Payouts" ALTER COLUMN "Currency" SET NOT NULL;

-- Remove Currency and Limit from PullPayment Blob, and put it into the columns in the table
ALTER TABLE "PullPayments" ADD COLUMN "Currency" TEXT;
UPDATE "PullPayments" SET "Currency" = "Blob"->>'Currency';
ALTER TABLE "PullPayments" ALTER COLUMN "Currency" SET NOT NULL;
ALTER TABLE "PullPayments" ADD COLUMN "Limit" NUMERIC;
UPDATE "PullPayments" SET "Limit" = ("Blob"->>'Limit')::NUMERIC;
ALTER TABLE "PullPayments" ALTER COLUMN "Limit" SET NOT NULL;

-- Remove unused properties, rename SupportedPaymentMethods, and fix legacy payment methods IDs
UPDATE "PullPayments" SET 
    "Blob" = jsonb_set(
	    "Blob" - 'SupportedPaymentMethods' - 'Limit' - 'Currency' - 'Period',
	    '{SupportedPayoutMethods}',
	    (SELECT jsonb_agg(to_jsonb(
		    CASE
	                WHEN split_part(value::TEXT, '_', 2) = 'LightningLike' THEN split_part(value::TEXT, '_', 1) || '-LN'
        	        ELSE split_part(value::TEXT, '_', 1) || '-CHAIN'
	            END))
	    FROM jsonb_array_elements_text("Blob"->'SupportedPaymentMethods') AS value
	));

--Remove "Amount" and "CryptoAmount" from Payout Blob, and put it into the columns in the table
-- Respectively "OriginalAmount" and "Amount"

ALTER TABLE "Payouts" ADD COLUMN "Amount" NUMERIC;
UPDATE "Payouts" SET "Amount" = ("Blob"->>'CryptoAmount')::NUMERIC;

ALTER TABLE "Payouts" ADD COLUMN "OriginalAmount" NUMERIC;
UPDATE "Payouts" SET "OriginalAmount" = ("Blob"->>'Amount')::NUMERIC;
ALTER TABLE "Payouts" ALTER COLUMN "OriginalAmount" SET NOT NULL;

ALTER TABLE "Payouts" ADD COLUMN "OriginalCurrency" TEXT;


UPDATE "Payouts" p
SET
    "OriginalCurrency" = "Currency",
    "Blob" = "Blob" - 'Amount' - 'CryptoAmount'
WHERE "PullPaymentDataId" IS NULL AND "OriginalCurrency" IS NULL;

UPDATE "Payouts" p
SET 
    "OriginalCurrency" = pp."Currency"
FROM "PullPayments" pp
WHERE "OriginalCurrency" IS NULL AND pp."Id"=p."PullPaymentDataId";

ALTER TABLE "Payouts" ALTER COLUMN "OriginalCurrency" SET NOT NULL;
