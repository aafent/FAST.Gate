# FAST.Gate — Roles & Abilities Registry

This file is the source of truth for all roles and abilities in the FAST ecosystem.
Changes are reviewed like code. Names are immutable after registration.

---

## Convention

### Roles
```
{app}.{role}          — app-specific role
fast.{role}           — global role across all FAST apps
```

### Abilities
```
{app}.{resource}.{action}    — app-specific ability
```

### Actions (fixed vocabulary)
| Action | Meaning |
|--------|---------|
| `read` | View / list |
| `write` | Create / update |
| `delete` | Delete |
| `admin` | Full management |

---

## Global roles

| Role | Description | Managed by |
|------|-------------|-----------|
| `fast.superadmin` | Full access to all FAST apps | FAST administrator |
| `fast.flowchart.user` | Access to FAST.FlowChart designer | FAST administrator |
| `fast.workflow.user` | Access to FAST.Workflow designer | FAST administrator |

## Application-specific roles

### ERP
| Role | Description |
|------|-------------|
| `erp.admin` | Full ERP access |
| `erp.accountant` | Access to accounting modules |
| `erp.viewer` | Read-only ERP access |

## Abilities (fine-grained)

### ERP
| Ability | Description |
|---------|-------------|
| `erp.accounts.delete` | Delete entries from General Ledger accounts |
| `erp.accounts.read` | Read General Ledger accounts |
| `erp.accounts.write` | Create/update General Ledger accounts |

---

## Adding a new role or ability

1. Add a row to this file in a PR
2. After merge, register in Logto console
3. Assign to relevant users/M2M apps in Logto
4. Update application authorization checks

## Deprecating

1. Mark row `[DEPRECATED yyyy-mm-dd]`
2. Notify affected teams
3. Remove from Logto only after all usages are removed
4. Delete row from this file
