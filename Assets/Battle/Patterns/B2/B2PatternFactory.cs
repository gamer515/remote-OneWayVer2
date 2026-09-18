using System;

public static class B2PatternFactory
{
    public static IBattlePattern Create(int id)
    {
        switch (id)
        {
            case 1:
                return new B2Pattern01();

            // 두 번째 패턴을 만든 뒤 추가:
            // case 2:
            //     return new B2Pattern02();

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(id),
                    id,
                    "등록되지 않은 B2 패턴입니다.");
        }
    }
}