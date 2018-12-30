Vue.component('contribute', {
    props: ["targetCurrency", "active", "perks", "inModal"],
    template: "#contribute-template"
});

Vue.component('perks', {
    props: ["perks", "targetCurrency", "active", "inModal"],
    template: "#perks-template"
});

Vue.component('perk', {
    props: ["perk", "targetCurrency", "active", "inModal"],
    template: "#perk-template",
    data: function () {
        return {
            amount: null,
            expanded: false
        }
    },
    methods: {
        onContributeFormSubmit: function (e) {
            if (e) {
                e.preventDefault();
            }
            if(!this.active){
                return;
            }
            eventAggregator.$emit("contribute", {amount: this.amount, choiceKey: this.choiceKey});
        }
    },
    mounted: function(){
        this.amount = this.perk.price.value;
    }
});


