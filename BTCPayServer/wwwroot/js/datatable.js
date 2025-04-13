(function () {
    // Given sorted data, build a tabular data of given groups and aggregates.
    function groupBy(groupIndices, aggregatesIndices, data) {
        const summaryRows = [];
        let summaryRow = null;
        for (let i = 0; i < data.length; i++) {
            if (summaryRow) {
                for (let gi = 0; gi < groupIndices.length; gi++) {
                    if (summaryRow[gi] !== data[i][groupIndices[gi]]) {
                        summaryRows.push(summaryRow);
                        summaryRow = null;
                        break;
                    }
                }
            }
            if (!summaryRow) {
                summaryRow = new Array(groupIndices.length + aggregatesIndices.length);
                for (let gi = 0; gi < groupIndices.length; gi++) {
                    summaryRow[gi] = data[i][groupIndices[gi]];
                }
                summaryRow.fill(new Decimal(0), groupIndices.length);
            }
            for (let ai = 0; ai < aggregatesIndices.length; ai++) {
                const v = data[i][aggregatesIndices[ai]];
                // TODO: support other aggregate functions
                if (typeof (v) === 'object' && v.v) {
                    // Amount in the format of `{ v: "1.0000001", d: 8 }`, where v is decimal string and `d` is divisibility
                    const agg = summaryRow[groupIndices.length + ai];
                    let d = v.d;
                    let val = new Decimal(v.v);
                    if (typeof (agg) === 'object' && agg.v) {
                        d = Math.max(d, agg.d);
                        val = agg.v.plus(val);
                    }
                    summaryRow[groupIndices.length + ai] = {
                        v: val,
                        d: d
                    };
                } else {
                    const val = new Decimal(v);
                    summaryRow[groupIndices.length + ai] = summaryRow[groupIndices.length + ai].plus(val);
                }
            }
        }
        if (summaryRow) {
            summaryRows.push(summaryRow);
        }
        return summaryRows;
    }

    // Sort tabular data by the column indices
    function byColumns(columnIndices) {
        return (a, b) => {
            for (let i = 0; i < columnIndices.length; i++) {
                const fieldIndex = columnIndices[i];
                if (!a[fieldIndex]) return 1;
                if (!b[fieldIndex]) return -1;
                if (a[fieldIndex] < b[fieldIndex]) return -1;
                if (a[fieldIndex] > b[fieldIndex]) return 1;
            }
            return 0;
        }
    }

    // Build a representation of the HTML table's data 'rows' from the tree of nodes.
    function buildRows(node, rows) {
        if (node.children.length === 0 && node.level !== 0) {
            const row =
            {
                values: node.values,
                groups: [],
                isTotal: node.isTotal,
                rLevel: node.rLevel
            };
            for (let i = 0; i < row.values.length; i++) {
                if (typeof row.values[i] === 'number') {
                    row.values[i] = new Decimal(row.values[i]);
                }
            }
            if (!node.isTotal)
                row.groups.push({ name: node.groups[node.groups.length - 1], rowCount: node.leafCount })
            let parent = node.parent, n = node;
            while (parent && parent.level !== 0 && parent.children[0] === n) {
                row.groups.push({ name: parent.groups[parent.groups.length - 1], rowCount: parent.leafCount })
                n = parent;
                parent = parent.parent;
            }
            row.groups.reverse();
            rows.push(row);
        }
        for (let i = 0; i < node.children.length; i++) {
            buildRows(node.children[i], rows);
        }
    }

    // Add a leafCount property, the number of leaf below each nodes
    // Remove total if there is only one child outside of the total
    function visitTree(node) {
        node.leafCount = 0;
        if (node.children.length === 0) {
            node.leafCount++;
            return;
        }
        for (let i = 0; i < node.children.length; i++) {
            visitTree(node.children[i]);
            node.leafCount += node.children[i].leafCount;
        }
        // Remove total if there is only one child outside of the total
        if (node.children.length === 2 && node.children[0].isTotal) {
            node.children.shift();
            node.leafCount--;
        }
    }

    // Build a tree of nodes from all the group levels.
    function makeTree(totalLevels, parent, groupLevels, level) {
        if (totalLevels.indexOf(level - 1) !== -1) {
            parent.children.push({
                parent: parent,
                groups: parent.groups,
                values: parent.values,
                children: [],
                level: level,
                rLevel: groupLevels.length - level,
                isTotal: true
            });
        }
        for (let i = 0; i < groupLevels[level].length; i++) {
            let foundFirst = false;
            let groupData = groupLevels[level][i];
            let gotoNextRow = false;
            let stop = false;
            for (let gi = 0; gi < parent.groups.length; gi++) {
                if (parent.groups[gi] !== groupData[gi]) {
                    if (foundFirst) {
                        stop = true;
                    }
                    else {
                        gotoNextRow = true;
                        foundFirst = true;
                        break;
                    }
                }
            }
            if (stop)
                break;
            if (gotoNextRow)
                continue;
            const node =
            {
                parent: parent,
                groups: groupData.slice(0, level),
                values: groupData.slice(level),
                children: [],
                level: level,
                rLevel: groupLevels.length - level
            };
            parent.children.push(node);
            if (groupLevels.length > level + 1)
                makeTree(totalLevels, node, groupLevels, level + 1);
        }
    }

    function applyFilters(rows, fields, filterStrings) {
        if (!filterStrings || filterStrings.length === 0)
            return rows;
        // filterStrings are aggregated into one filter function:
        // filter(){ return filter1 && filter2 && filter3; }
        var newData = [];
        var o = {};
        eval('function filter() {return ' + filterStrings.join(' && ') + ';}');
        // For each row, build a JSON objects representing it, and evaluate it on the fitler
        for (var i = 0; i < rows.length; i++) {
            for (var fi = 0; fi < fields.length; fi++) {
                o[fields[fi]] = rows[i][fi];
            }
            if (!filter.bind(o)())
                continue;
            newData.push(rows[i]);
        }
        return newData;
    }


    function clone(a) {
        return Array.from(a, subArray => [...subArray]);
    }

    function createTable(summaryDefinition, fields, rows) {
        rows = clone(rows);
        var groupIndices = summaryDefinition.groups.map(g => fields.findIndex((a) => a === g)).filter(g => g !== -1);
        var aggregatesIndices = summaryDefinition.aggregates.map(g => fields.findIndex((a) => a === g)).filter(g => g !== -1);
        aggregatesIndices = aggregatesIndices.filter(g => g !== -1);
        // Filter rows
        rows = applyFilters(rows, fields, summaryDefinition.filters);

        // Sort by group columns
        rows.sort(byColumns(groupIndices));

        // Group data represent tabular data of all the groups and aggregates given the data.
        // [Region, Crypto, PaymentType]
        var groupRows = groupBy(groupIndices, aggregatesIndices, rows);

        // There will be several level of aggregation
        // For example, if you have 3 groups: [Region, Crypto, PaymentType] then you have 4 group data.
        // [Region, Crypto, PaymentType]
        // [Region, Crypto]
        // [Region]
        // []
        var groupLevels = [];
        groupLevels.push(groupRows);

        // We build the group rows with less columns
        // Those builds the level:
        // [Region, Crypto], [Region] and []
        for (var i = 1; i < groupIndices.length + 1; i++) {

            // We are grouping the group data.
            // For our example of 3 groups and 2 aggregate2, then:
            // First iteration: newGroupIndices = [0, 1], newAggregatesIndices = [3, 4]
            // Second iteration: newGroupIndices = [0], newAggregatesIndices = [2, 3]
            // Last iteration: newGroupIndices = [], newAggregatesIndices = [1, 2]
            var newGroupIndices = [];
            for (var gi = 0; gi < groupIndices.length - i; gi++) {
                newGroupIndices.push(gi);
            }
            var newAggregatesIndices = [];
            for (var ai = 0; ai < aggregatesIndices.length; ai++) {
                newAggregatesIndices.push(newGroupIndices.length + 1 + ai);
            }
            // Group the group rows
            groupRows = groupBy(newGroupIndices, newAggregatesIndices, groupRows);
            groupLevels.push(groupRows);
        }

        // Put the highest level ([]) on top
        groupLevels.reverse();

        var root =
        {
            parent: null,
            groups: [],
            // Note that the top group data always have one row aggregating all
            values: groupLevels[0][0],
            children: [],
            // level=0 means the root, it increments 1 each level
            level: 0,
            // rlevel is the reverse. It starts from the highest level and goes down to 0
            rLevel: groupLevels.length
        };
        // Which levels will have a total row
        let totalLevels = [];
        if (summaryDefinition.totals) {
            totalLevels = summaryDefinition.totals.map(g => summaryDefinition.groups.findIndex((a) => a === g) + 1).filter(a => a !== 0);
        }
        // Build the tree of nodes
        makeTree(totalLevels, root, groupLevels, 1);

        // Add a leafCount property to each node, it is the number of leaf below each nodes.
        visitTree(root);

        // Create a representation that can easily be bound to VueJS
        var rows = [];
        buildRows(root, rows);
        return {
            groups: summaryDefinition.groups,
            aggregates: summaryDefinition.aggregates,
            hasGrandTotal: root.values && summaryDefinition.hasGrandTotal,
            grandTotalValues: root.values,
            rows: rows
        };
    }

    window.clone = clone;
    window.createTable = createTable;
})();
