CREATE OR REPLACE FUNCTION get_orderid(invoice_blob jsonb)
RETURNS text AS $$
    SELECT invoice_blob->'metadata'->>'orderId';
$$ LANGUAGE sql IMMUTABLE;

CREATE OR REPLACE FUNCTION get_itemcode(invoice_blob jsonb)
RETURNS text AS $$
    SELECT invoice_blob->'metadata'->>'itemCode';
$$ LANGUAGE sql IMMUTABLE;

CREATE INDEX IF NOT EXISTS "IX_Invoices_Metadata_OrderId" ON "Invoices" (get_orderid("Blob2")) WHERE get_orderid("Blob2") IS NOT NULL;
CREATE INDEX IF NOT EXISTS "IX_Invoices_Metadata_ItemCode" ON "Invoices" (get_itemcode("Blob2")) WHERE get_itemcode("Blob2") IS NOT NULL;
