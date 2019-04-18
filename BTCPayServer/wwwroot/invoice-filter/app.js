var app = null;
var eventAggregator = new Vue();

addLoadEvent(function (ev) {
    document.getElementById("help-toggle").hidden = true;
    if(document.getElementById("invoice-filter-app") == null){
        return;
    }
    app = new Vue({
        el: '#invoice-filter-app',
        data: function () {
            return {
                srvModel: window.srvModel,
                textSearch: "",
                count: 0,
                filters: [],
                inputEvaluated: '',
                exceptionStatusOptions: [
                    {key:"paidPartial", text: "Paid partially"},
                    {key:"paidLate", text: "Paid late"},
                    {key:"paidOver", text: "Paid over"},
                    {key:"marked", text: "Marked"},
                    {key:"false", text: "None"}
                ],
                statusOptions: [
                    {key:"new", text: "New"},
                    {key:"paid", text: "Paid"},
                    {key:"confirmed", text: "Confirmed"},
                    {key:"invalid", text: "Invalid"},
                    {key:"expired", text: "Expired"},
                    {key:"complete", text: "Complete"}
                ]
            }
        },
        computed: {
            
            availableStatusFilters: function(){
                var statuses = this.statusOptions.slice();
                var filterValues = this.getFilterValues(this.statusFilters);
                this.filterAvailableOptions(statuses, filterValues);
                return statuses;
                
            },
            availableExceptionStatusFilters: function(){
                var statuses = this.exceptionStatusOptions.slice();
                var filterValues = this.getFilterValues(this.statusFilters);
                this.filterAvailableOptions(statuses, filterValues);
                return statuses;

            },
            unusualFilters: function(){
                return this.getSpecificFilters("unusual");
            },
            exceptionStatusFilters: function(){
                return this.getSpecificFilters("exceptionstatus");
            },
            storeFilters: function(){
                return this.getSpecificFilters("storeid");
            },
            statusFilters: function(){
                return this.getSpecificFilters("status");
            },
            itemCodeFilters: function(){
                return this.getSpecificFilters("itemcode");
            },
            orderFilters: function(){
                return this.getSpecificFilters("orderid");
            },
            searchTerm: function() {
                var result = "";
                result += this.textSearch;

                for (i = 0; i < this.filters.length; i++) {
                    var currentFilter = this.filters[i];
                    if(currentFilter.value == null || currentFilter.value ==""){
                        continue;
                    }
                    result += " " + currentFilter.key + ":" + currentFilter.value;
                }
                return result;
            }
        },
        mounted: function () {
            this.parseSearchString(this.srvModel.searchTerm);
            this.count = this.srvModel.count;
        },
        methods: {
            hasEmptyFilter: function(filter){
                                return this.getEmptyFilterIndex(filter) >= 0;
            },
            getEmptyFilterIndex: function(filter){
                var filters = this.getSpecificFilters(filter);
                for (var i = 0; i < filters.length; i++) {
                    if(filters[i].value == null || filters[i].value ===""){
                        return i;
                    }
                }
                return -1;
            },
            getOptionFromKey: function(options, key){
                for (var j = 0; j < options.length; j++) {
                    if(options[j].key === key ){
                        return options[j];
                    }
                }
            },
            filterAvailableOptions: function (options, filterValues){
                for (var i = 0; i < filterValues.length; i++) {
                    var index = -1;
                    for (var j = 0; j < options.length; j++) {
                        if(options[j].key === filterValues[i] ){
                            index = j;
                            break;
                        }
                    }
                    if(index > -1){
                        options.splice(index, 1);
                    }
                }
            },
            getFilterValues: function(filters){
                return filters.map(function(i){
                    return i.value;
                });
            },
            setValue: function(index, value) {
                var currentValue = this.filters[index];
                Vue.set(this.filters, index, {key: currentValue.key, value: value});
                console.log(this.searchTerm);
            },
            parseSearchString: function(str){
                var result = []
                if(str == null){
                    str = "";
                }
                var split = str.trim().split(" ");
                var textStr = str.trim();
                for (i = 0; i< split.length; i++ ){
                    var currentSplit = split[i];
                    var currentSplitValues = currentSplit.split(":").filter(function (x)  {
                        return x != "";
                    });
                    if(currentSplitValues.length === 2){
                        result.push({key: currentSplitValues[0].toLowerCase(), value: currentSplitValues[1]});   
                        textStr = textStr.replace(currentSplit, "");
                    }
                }
                this.textSearch = textStr.trim();
                this.filters = result;
                
            },
            getSpecificFilters: function(key){
                var result = [];
                for (i = 0; i< this.filters.length; i++){
                    var currentFilter = this.filters[i];
                    if(currentFilter.key == key){
                        result.push({ key: currentFilter.key, value: currentFilter.value, index: i });
                    }
                } 
                return result;
            }
        }
    });
});

