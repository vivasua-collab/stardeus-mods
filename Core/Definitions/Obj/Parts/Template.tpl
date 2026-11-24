{
    "Layer" : "FreeEntities",
    "IgnoreMarket" : true,
    "ParentId" : "Obj/Parts/{{parentDefId}}",
    "NameKey" : "{{nameKey}}",
{{researchable}}
    "Components" : [
        {
            "Component" : "ObjGraphics",
            "Properties" : [
                { "Key" : "Graphic", "String": "{{graphicId}}" }
            ]
        },
        {
            "Component" : "BodyPart",
            "Properties" : [
                { "Key" : "BodyPartDefId", "String" : "{{partDefId}}" }
            ]
        }
    ]
}