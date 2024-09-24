CREATE OR REPLACE FUNCTION get_prompt(invoice_blob JSONB, payment_method_id TEXT)
RETURNS JSONB AS $$
    SELECT invoice_blob->'prompts'->payment_method_id
$$ LANGUAGE sql IMMUTABLE;


CREATE OR REPLACE FUNCTION get_monitored_invoices(payment_method_id TEXT)
RETURNS TABLE (invoice_id TEXT, payment_id TEXT) AS $$
WITH cte AS (
-- Get all the invoices which are pending. Even if no payments.
SELECT i."Id" invoice_id, p."Id" payment_id FROM "Invoices" i LEFT JOIN "Payments" p ON i."Id" = p."InvoiceDataId"
        WHERE is_pending(i."Status")
UNION ALL
-- For invoices not pending, take all of those which have pending payments
SELECT i."Id", p."Id" FROM "Invoices" i INNER JOIN "Payments" p ON i."Id" = p."InvoiceDataId"
        WHERE is_pending(p."Status") AND NOT is_pending(i."Status"))
SELECT cte.* FROM cte
LEFT JOIN "Payments" p ON cte.payment_id=p."Id"
LEFT JOIN "Invoices" i ON cte.invoice_id=i."Id"
WHERE (p."Type" IS NOT NULL AND p."Type" = payment_method_id) OR
      (p."Type" IS NULL AND get_prompt(i."Blob2", payment_method_id) IS NOT NULL AND (get_prompt(i."Blob2", payment_method_id)->'activated')::BOOLEAN IS NOT FALSE);
$$ LANGUAGE SQL STABLE;
