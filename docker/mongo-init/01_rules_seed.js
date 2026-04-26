db = db.getSiblingDB('catalog');

// Drop the collection first
db.entity_rules.drop();

// Insert 36 business rules (6-7 per entity)

// Counterparty rules (7)
db.entity_rules.insertMany([
  {
    "ruleId": "CNTP-001",
    "entityCode": "counterparty",
    "ruleName": "LEI Format Validation",
    "ruleType": "MANDATORY",
    "description": "LEI must conform to ISO 17442 — 20 alphanumeric characters",
    "conditions": [],
    "severity": "ERROR",
    "owner": "Data Governance",
    "regulatoryReference": "GLEIF",
    "isActive": true
  },
  {
    "ruleId": "CNTP-002",
    "entityCode": "counterparty",
    "ruleName": "Legal Name Required",
    "ruleType": "MANDATORY",
    "description": "Legal name must not be empty",
    "conditions": [],
    "severity": "ERROR",
    "owner": "Data Governance",
    "regulatoryReference": null,
    "isActive": true
  },
  {
    "ruleId": "CNTP-003",
    "entityCode": "counterparty",
    "ruleName": "Status Valid Enum",
    "ruleType": "MANDATORY",
    "description": "Status must be one of: PROSPECT, ACTIVE, SUSPENDED, TERMINATED",
    "conditions": [],
    "severity": "ERROR",
    "owner": "Data Operations",
    "regulatoryReference": null,
    "isActive": true
  },
  {
    "ruleId": "CNTP-004",
    "entityCode": "counterparty",
    "ruleName": "FATCA Required for Active",
    "ruleType": "MANDATORY",
    "description": "FATCA status required for all active counterparties",
    "conditions": [],
    "severity": "ERROR",
    "owner": "Tax Compliance",
    "regulatoryReference": "FATCA",
    "isActive": true
  },
  {
    "ruleId": "CNTP-005",
    "entityCode": "counterparty",
    "ruleName": "LEI Required for Legal Entity",
    "ruleType": "CONDITIONAL",
    "description": "IF entity_type=LEGAL_ENTITY THEN LEI is mandatory",
    "conditions": [
      {
        "attributeCode": "entity_type",
        "operator": "EQUALS",
        "value": "LEGAL_ENTITY"
      }
    ],
    "severity": "ERROR",
    "owner": "Data Governance",
    "regulatoryReference": "GLEIF",
    "isActive": true
  },
  {
    "ruleId": "CNTP-006",
    "entityCode": "counterparty",
    "ruleName": "EDD for High-Risk Countries",
    "ruleType": "CONDITIONAL",
    "description": "IF incorporation_country is FATF high-risk THEN enhanced due diligence required",
    "conditions": [
      {
        "attributeCode": "incorporation_country",
        "operator": "IN_HIGH_RISK_LIST",
        "value": "FATF_HIGH_RISK"
      }
    ],
    "severity": "WARNING",
    "owner": "Compliance",
    "regulatoryReference": "FATF",
    "isActive": true
  },
  {
    "ruleId": "CNTP-007",
    "entityCode": "counterparty",
    "ruleName": "KYC Review Required",
    "ruleType": "CONDITIONAL",
    "description": "IF kyc_expiry_date < today THEN counterparty review must be triggered",
    "conditions": [
      {
        "attributeCode": "kyc_expiry_date",
        "operator": "LESS_THAN",
        "value": "TODAY"
      }
    ],
    "severity": "WARNING",
    "owner": "KYC Operations",
    "regulatoryReference": null,
    "isActive": true
  },

  // ClientAccount rules (6)
  {
    "ruleId": "CACC-001",
    "entityCode": "clientaccount",
    "ruleName": "Account Number Unique",
    "ruleType": "MANDATORY",
    "description": "Account number must be unique per counterparty",
    "conditions": [],
    "severity": "ERROR",
    "owner": "Data Operations",
    "regulatoryReference": null,
    "isActive": true
  },
  {
    "ruleId": "CACC-002",
    "entityCode": "clientaccount",
    "ruleName": "Currency Valid",
    "ruleType": "MANDATORY",
    "description": "Currency must be a valid ISO 4217 code",
    "conditions": [],
    "severity": "ERROR",
    "owner": "Reference Data",
    "regulatoryReference": "ISO 4217",
    "isActive": true
  },
  {
    "ruleId": "CACC-003",
    "entityCode": "clientaccount",
    "ruleName": "Counterparty Active",
    "ruleType": "MANDATORY",
    "description": "Referenced counterparty must have status ACTIVE",
    "conditions": [],
    "severity": "ERROR",
    "owner": "Data Governance",
    "regulatoryReference": null,
    "isActive": true
  },
  {
    "ruleId": "CACC-004",
    "entityCode": "clientaccount",
    "ruleName": "Omnibus Segregation",
    "ruleType": "CONDITIONAL",
    "description": "IF account_type=OMNIBUS THEN segregation disclosure is required",
    "conditions": [
      {
        "attributeCode": "account_type",
        "operator": "EQUALS",
        "value": "OMNIBUS"
      }
    ],
    "severity": "ERROR",
    "owner": "Compliance",
    "regulatoryReference": "MiFID II",
    "isActive": true
  },
  {
    "ruleId": "CACC-005",
    "entityCode": "clientaccount",
    "ruleName": "EMIR Flag",
    "ruleType": "CONDITIONAL",
    "description": "IF regulatory_regime=EMIR THEN emir_flag must be explicitly set",
    "conditions": [
      {
        "attributeCode": "regulatory_regime",
        "operator": "EQUALS",
        "value": "EMIR"
      }
    ],
    "severity": "ERROR",
    "owner": "Compliance",
    "regulatoryReference": "EMIR",
    "isActive": true
  },
  {
    "ruleId": "CACC-006",
    "entityCode": "clientaccount",
    "ruleName": "External Custody Agreement",
    "ruleType": "CONDITIONAL",
    "description": "IF custodian is external entity THEN custody agreement document is required",
    "conditions": [
      {
        "attributeCode": "custodian",
        "operator": "IS_EXTERNAL",
        "value": "true"
      }
    ],
    "severity": "WARNING",
    "owner": "Operations",
    "regulatoryReference": null,
    "isActive": true
  },

  // Book rules (7)
  {
    "ruleId": "BOOK-001",
    "entityCode": "book",
    "ruleName": "Book Code Globally Unique",
    "ruleType": "MANDATORY",
    "description": "Book code must be unique across all books",
    "conditions": [],
    "severity": "ERROR",
    "owner": "Data Operations",
    "regulatoryReference": null,
    "isActive": true
  },
  {
    "ruleId": "BOOK-002",
    "entityCode": "book",
    "ruleName": "Legal Entity Valid",
    "ruleType": "MANDATORY",
    "description": "Legal entity must reference a valid active counterparty",
    "conditions": [],
    "severity": "ERROR",
    "owner": "Data Governance",
    "regulatoryReference": null,
    "isActive": true
  },
  {
    "ruleId": "BOOK-003",
    "entityCode": "book",
    "ruleName": "Asset Class Required",
    "ruleType": "MANDATORY",
    "description": "Asset class must be specified for all books",
    "conditions": [],
    "severity": "ERROR",
    "owner": "Data Operations",
    "regulatoryReference": null,
    "isActive": true
  },
  {
    "ruleId": "BOOK-004",
    "entityCode": "book",
    "ruleName": "FX Booking System",
    "ruleType": "CONDITIONAL",
    "description": "IF book_type=TRADING AND asset_class=FX THEN booking_system must be SYSTEM_FX",
    "conditions": [
      {
        "attributeCode": "book_type",
        "operator": "EQUALS",
        "value": "TRADING"
      },
      {
        "attributeCode": "asset_class",
        "operator": "EQUALS",
        "value": "FX"
      }
    ],
    "severity": "ERROR",
    "owner": "Technology",
    "regulatoryReference": null,
    "isActive": true
  },
  {
    "ruleId": "BOOK-005",
    "entityCode": "book",
    "ruleName": "FRTB Risk Limit",
    "ruleType": "CONDITIONAL",
    "description": "IF regulation_type=FRTB_TRADING THEN at least one risk limit must be set",
    "conditions": [
      {
        "attributeCode": "regulation_type",
        "operator": "EQUALS",
        "value": "FRTB_TRADING"
      }
    ],
    "severity": "ERROR",
    "owner": "Risk Management",
    "regulatoryReference": "FRTB",
    "isActive": true
  },
  {
    "ruleId": "BOOK-006",
    "entityCode": "book",
    "ruleName": "Limit Currency Required",
    "ruleType": "CONDITIONAL",
    "description": "IF book_limit is set THEN limit_currency must be provided",
    "conditions": [
      {
        "attributeCode": "book_limit",
        "operator": "IS_NOT_NULL",
        "value": ""
      }
    ],
    "severity": "ERROR",
    "owner": "Data Operations",
    "regulatoryReference": null,
    "isActive": true
  },
  {
    "ruleId": "BOOK-007",
    "entityCode": "book",
    "ruleName": "Archived Book No Trades",
    "ruleType": "CONDITIONAL",
    "description": "IF status=ARCHIVED THEN no new trades may be booked to this book",
    "conditions": [
      {
        "attributeCode": "status",
        "operator": "EQUALS",
        "value": "ARCHIVED"
      }
    ],
    "severity": "ERROR",
    "owner": "Data Operations",
    "regulatoryReference": null,
    "isActive": true
  },

  // SettlementInstruction rules (6)
  {
    "ruleId": "SSIN-001",
    "entityCode": "settlementinstruction",
    "ruleName": "Instruction Type Valid",
    "ruleType": "MANDATORY",
    "description": "Instruction type must be one of: DVP, FOP, RVP, DFP",
    "conditions": [],
    "severity": "ERROR",
    "owner": "Settlement Operations",
    "regulatoryReference": null,
    "isActive": true
  },
  {
    "ruleId": "SSIN-002",
    "entityCode": "settlementinstruction",
    "ruleName": "Currency Valid",
    "ruleType": "MANDATORY",
    "description": "Settlement currency must be a valid ISO 4217 code",
    "conditions": [],
    "severity": "ERROR",
    "owner": "Reference Data",
    "regulatoryReference": "ISO 4217",
    "isActive": true
  },
  {
    "ruleId": "SSIN-003",
    "entityCode": "settlementinstruction",
    "ruleName": "Counterparty Active",
    "ruleType": "MANDATORY",
    "description": "Referenced counterparty must have status ACTIVE",
    "conditions": [],
    "severity": "ERROR",
    "owner": "Data Governance",
    "regulatoryReference": null,
    "isActive": true
  },
  {
    "ruleId": "SSIN-004",
    "entityCode": "settlementinstruction",
    "ruleName": "DVP RVP BIC Required",
    "ruleType": "CONDITIONAL",
    "description": "IF instruction_type IN (DVP, RVP) THEN swift_bic OR iban is required",
    "conditions": [
      {
        "attributeCode": "instruction_type",
        "operator": "IN",
        "value": "DVP,RVP"
      }
    ],
    "severity": "ERROR",
    "owner": "Settlement Operations",
    "regulatoryReference": "SWIFT",
    "isActive": true
  },
  {
    "ruleId": "SSIN-005",
    "entityCode": "settlementinstruction",
    "ruleName": "CLS Currency Check",
    "ruleType": "CONDITIONAL",
    "description": "IF settlement_method=CLS THEN currency must be CLS eligible",
    "conditions": [
      {
        "attributeCode": "settlement_method",
        "operator": "EQUALS",
        "value": "CLS"
      }
    ],
    "severity": "ERROR",
    "owner": "Settlement Operations",
    "regulatoryReference": "CLS",
    "isActive": true
  },
  {
    "ruleId": "SSIN-006",
    "entityCode": "settlementinstruction",
    "ruleName": "DTCC Membership",
    "ruleType": "CONDITIONAL",
    "description": "IF clearing_house=DTCC THEN counterparty must have active DTCC membership",
    "conditions": [
      {
        "attributeCode": "clearing_house",
        "operator": "EQUALS",
        "value": "DTCC"
      }
    ],
    "severity": "ERROR",
    "owner": "Settlement Operations",
    "regulatoryReference": "DTCC",
    "isActive": true
  },

  // Country rules (5)
  {
    "ruleId": "CTRY-001",
    "entityCode": "country",
    "ruleName": "ISO2 Format",
    "ruleType": "MANDATORY",
    "description": "ISO 3166-1 alpha-2 code must be exactly 2 uppercase letters",
    "conditions": [],
    "severity": "ERROR",
    "owner": "Reference Data",
    "regulatoryReference": "ISO 3166-1",
    "isActive": true
  },
  {
    "ruleId": "CTRY-002",
    "entityCode": "country",
    "ruleName": "FATF Status Required",
    "ruleType": "MANDATORY",
    "description": "FATF risk status is required for all active countries",
    "conditions": [],
    "severity": "ERROR",
    "owner": "Compliance",
    "regulatoryReference": "FATF",
    "isActive": true
  },
  {
    "ruleId": "CTRY-003",
    "entityCode": "country",
    "ruleName": "Sanctions Status Required",
    "ruleType": "MANDATORY",
    "description": "Sanctions screening status must be set for all active countries",
    "conditions": [],
    "severity": "ERROR",
    "owner": "Compliance",
    "regulatoryReference": "OFAC/EU",
    "isActive": true
  },
  {
    "ruleId": "CTRY-004",
    "entityCode": "country",
    "ruleName": "High Risk Approval",
    "ruleType": "CONDITIONAL",
    "description": "IF fatf_status=HIGH_RISK THEN onboarding of new counterparties requires senior approval",
    "conditions": [
      {
        "attributeCode": "fatf_status",
        "operator": "EQUALS",
        "value": "HIGH_RISK"
      }
    ],
    "severity": "WARNING",
    "owner": "Compliance",
    "regulatoryReference": "FATF",
    "isActive": true
  },
  {
    "ruleId": "CTRY-005",
    "entityCode": "country",
    "ruleName": "Sanctions Compliance",
    "ruleType": "CONDITIONAL",
    "description": "IF sanctions_status != CLEAN THEN all transactions require compliance pre-approval",
    "conditions": [
      {
        "attributeCode": "sanctions_status",
        "operator": "NOT_EQUALS",
        "value": "CLEAN"
      }
    ],
    "severity": "ERROR",
    "owner": "Compliance",
    "regulatoryReference": "OFAC/EU",
    "isActive": true
  },

  // Currency rules (5)
  {
    "ruleId": "CURR-001",
    "entityCode": "currency",
    "ruleName": "ISO Code Format",
    "ruleType": "MANDATORY",
    "description": "ISO 4217 currency code must be exactly 3 uppercase letters",
    "conditions": [],
    "severity": "ERROR",
    "owner": "Reference Data",
    "regulatoryReference": "ISO 4217",
    "isActive": true
  },
  {
    "ruleId": "CURR-002",
    "entityCode": "currency",
    "ruleName": "Decimal Places Valid",
    "ruleType": "MANDATORY",
    "description": "Decimal places must be 0, 2 or 3 per ISO 4217",
    "conditions": [],
    "severity": "ERROR",
    "owner": "Reference Data",
    "regulatoryReference": "ISO 4217",
    "isActive": true
  },
  {
    "ruleId": "CURR-003",
    "entityCode": "currency",
    "ruleName": "Deliverable Flag Required",
    "ruleType": "MANDATORY",
    "description": "IsDeliverable flag must be explicitly set for all currencies",
    "conditions": [],
    "severity": "ERROR",
    "owner": "Reference Data",
    "regulatoryReference": null,
    "isActive": true
  },
  {
    "ruleId": "CURR-004",
    "entityCode": "currency",
    "ruleName": "NDF Settlement Currency",
    "ruleType": "CONDITIONAL",
    "description": "IF is_deliverable=false THEN settlement_currency must be specified (typically USD)",
    "conditions": [
      {
        "attributeCode": "is_deliverable",
        "operator": "EQUALS",
        "value": "false"
      }
    ],
    "severity": "ERROR",
    "owner": "Reference Data",
    "regulatoryReference": null,
    "isActive": true
  },
  {
    "ruleId": "CURR-005",
    "entityCode": "currency",
    "ruleName": "Deprecated Currency",
    "ruleType": "CONDITIONAL",
    "description": "IF status=DEPRECATED THEN no new accounts may be opened in this currency",
    "conditions": [
      {
        "attributeCode": "status",
        "operator": "EQUALS",
        "value": "DEPRECATED"
      }
    ],
    "severity": "ERROR",
    "owner": "Reference Data",
    "regulatoryReference": null,
    "isActive": true
  }
]);

// Confirm insertion
print("Inserted " + db.entity_rules.countDocuments({}) + " business rules");
