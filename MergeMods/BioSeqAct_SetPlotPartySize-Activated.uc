public event function Activated()
{
    local int nPartyCount;
    local BioGlobalVariableTable gv;
    local int I;
    local bool bValue;

    OutputLinks[0].bHasImpulse = TRUE;
    gv = getWorld().GetGlobalVariables();
    nPartyCount = 0;
    for (I = 0; I < 12; I++)
    {
        bValue = gv.GetBool(33 + I);
        if (bValue)
        {
            nPartyCount++;
        }
    }
    if (gv.GetBool(6961))
    {
        nPartyCount++;
    }
    gv.SetInt(22, nPartyCount);
}
